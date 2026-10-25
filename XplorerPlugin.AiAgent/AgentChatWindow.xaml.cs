using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Xbim.Common;
using Xbim.Presentation.XplorerPluginSystem;
using XplorerPlugin.AiAgent.Agent;
using XplorerPlugin.AiAgent.Chat;
using XplorerPlugin.AiAgent.Llm;
using XplorerPlugin.AiAgent.Settings;
using XplorerPlugin.AiAgent.Tools;

namespace XplorerPlugin.AiAgent
{
    /// <summary>
    /// Chat panel with the AI agent. Docked as a side panel in XbimXplorer.
    /// </summary>
    [XplorerUiElement(PluginWindowUiContainerEnum.LayoutAnchorable, PluginWindowActivation.OnMenu, "AI Agent")]
    public partial class AgentChatWindow : IXbimXplorerPluginWindow
    {
        private IXbimXplorerPluginMasterWindow _xpWindow;
        private AgentSettings _settings;
        private OpenAiClient _client;
        private AgentRunner _runner;
        private CancellationTokenSource _cts;
        private ChatMessageViewModel _streamingMessage;
        private readonly StringBuilder _streamBuffer = new StringBuilder();
        private readonly DispatcherTimer _streamFlushTimer;
        private bool _streamDirty;
        private bool _forceNextScroll = true;
        private ScrollViewer _messagesScroll;

        public ObservableCollection<ChatMessageViewModel> Messages { get; } =
            new ObservableCollection<ChatMessageViewModel>();

        public AgentChatWindow()
        {
            InitializeComponent();
            _streamFlushTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _streamFlushTimer.Tick += StreamFlushTick;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            _settings = AgentSettings.Load();
            ApplySettings();
        }

        private void ApplySettings()
        {
            _client = new OpenAiClient
            {
                BaseUrl = _settings.BaseUrl,
                ApiKey = _settings.ApiKey,
                Model = _settings.Model,
                Temperature = _settings.Temperature
            };
            _runner = new AgentRunner(_client, _settings)
            {
                ToolExecutor = ExecuteToolOnUiThread,
                OnAssistantDelta = OnAssistantDelta,
                OnToolActivity = OnToolActivity
            };
            var queryTools = new ModelQueryTools(() => Model, _settings, SelectEntity);
            foreach (var tool in queryTools.GetAll())
                _runner.RegisterTool(tool);
            var reportTools = new ReportTools(() => Model, () => _xpWindow?.GetOpenedModelFileName(), _settings);
            reportTools.ReportGenerated += OnReportGenerated;
            _runner.RegisterTool(reportTools.GetGenerateReportTool());
            _runner.SetSystemPrompt(SystemPrompt.Build(_xpWindow?.GetOpenedModelFileName(), _settings.SystemPromptExtra));
        }

        // ---------------------------------------------------------------- IXbimXplorerPluginWindow

        public string WindowTitle => "AI Agent";

        public void BindUi(IXbimXplorerPluginMasterWindow mainWindow)
        {
            _xpWindow = mainWindow;
            SetBinding(SelectedItemProperty, new System.Windows.Data.Binding("SelectedItem")
            {
                Source = mainWindow,
                Mode = System.Windows.Data.BindingMode.OneWay
            });
            SetBinding(ModelProperty, new System.Windows.Data.Binding()); // inherits the model from the main window DataContext
        }

        public IPersistEntity SelectedItem
        {
            get => (IPersistEntity)GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register("SelectedItem", typeof(IPersistEntity), typeof(AgentChatWindow),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

        public IModel Model
        {
            get => (IModel)GetValue(ModelProperty);
            set => SetValue(ModelProperty, value);
        }

        public static readonly DependencyProperty ModelProperty =
            DependencyProperty.Register("Model", typeof(IModel), typeof(AgentChatWindow),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, OnModelChanged));

        private static void OnModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var window = (AgentChatWindow)d;
            window.ModelChanged();
        }

        private void ModelChanged()
        {
            var file = _xpWindow?.GetOpenedModelFileName();
            ModelStatusLabel.Text = string.IsNullOrEmpty(file)
                ? (Model != null ? "Model open (file name unknown)" : "No model open")
                : "Model: " + Path.GetFileName(file);
            if (_runner != null)
                _runner.SetSystemPrompt(SystemPrompt.Build(file, _settings?.SystemPromptExtra));
        }

        // ---------------------------------------------------------------- chat flow

        private async void SendClick(object sender, RoutedEventArgs e)
        {
            await SendAsync();
        }

        private async void InputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                await SendAsync();
            }
        }

        private async Task SendAsync()
        {
            var text = (InputBox.Text ?? "").Trim();
            if (text.Length == 0 || _runner == null)
                return;

            InputBox.Text = "";
            SetBusy(true);
            _cts = new CancellationTokenSource();

            AddMessage(ChatRole.User, "You", text);

            var assistantMsg = AddMessage(ChatRole.Assistant, "Agent", "");
            assistantMsg.IsStreaming = true;
            _streamingMessage = assistantMsg;
            _streamBuffer.Clear();
            _streamDirty = false;
            _forceNextScroll = true;

            try
            {
                var reply = await Task.Run(() => _runner.RunAsync(text, _cts.Token), _cts.Token);
                assistantMsg.Text = string.IsNullOrEmpty(reply) ? "(empty reply)" : reply;
                StatusText.Text = "";
            }
            catch (OperationCanceledException)
            {
                assistantMsg.Text = string.IsNullOrEmpty(assistantMsg.Text) ? "(stopped)" : assistantMsg.Text + "\n\n(stopped)";
                StatusText.Text = "Stopped";
            }
            catch (Exception ex)
            {
                assistantMsg.IsStreaming = false;
                _streamingMessage = null;
                AddMessage(ChatRole.Error, "Error", "LLM request failed: " + ex.Message);
            }
            finally
            {
                _streamFlushTimer.Stop();
                _streamDirty = false;
                assistantMsg.IsStreaming = false;
                _streamingMessage = null;
                ToolActivityText.Visibility = Visibility.Collapsed;
                SetBusy(false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void OnAssistantDelta(string delta)
        {
            // background thread
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_streamingMessage == null)
                    return;
                _streamBuffer.Append(delta);
                _streamDirty = true;
                if (!_streamFlushTimer.IsEnabled)
                    _streamFlushTimer.Start();
            }), DispatcherPriority.Background);
        }

        private void StreamFlushTick(object sender, EventArgs e)
        {
            if (!_streamDirty || _streamingMessage == null)
            {
                _streamFlushTimer.Stop();
                return;
            }
            _streamDirty = false;
            var stick = _forceNextScroll || IsNearBottom();
            _forceNextScroll = false;
            _streamingMessage.Text = _streamBuffer.ToString();
            if (stick)
                MessagesList.ScrollIntoView(_streamingMessage);
        }

        private bool IsNearBottom()
        {
            var sv = _messagesScroll ?? (_messagesScroll = FindScrollViewer(MessagesList));
            if (sv == null)
                return true;
            return sv.VerticalOffset >= sv.ScrollableHeight - 48;
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root is ScrollViewer sv)
                return sv;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var result = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (result != null)
                    return result;
            }
            return null;
        }

        private void OnToolActivity(string toolName, string argsPreview)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ToolActivityText.Text = "Tool: " + toolName + " " + argsPreview;
                ToolActivityText.Visibility = Visibility.Visible;
            }));
        }

        /// <summary>Executed by AgentRunner on a worker thread; marshals to the UI thread for model access.</summary>
        private string ExecuteToolOnUiThread(AgentTool tool, System.Text.Json.Nodes.JsonNode args)
        {
            return Dispatcher.Invoke(() => tool.Execute(args), DispatcherPriority.Normal);
        }

        private void OnReportGenerated(string path, string title)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var msg = AddMessage(ChatRole.Report, "Report",
                    "Report saved to:\n" + path);
                msg.ReportPath = path;
                StatusText.Text = "Report saved: " + Path.GetFileName(path);
            }));
        }

        private void SelectEntity(IPersistEntity entity)
        {
            if (_xpWindow != null)
                _xpWindow.SelectedItem = entity;
        }

        private ChatMessageViewModel AddMessage(ChatRole role, string author, string text)
        {
            var msg = new ChatMessageViewModel { Role = role, Author = author, Text = text };
            Messages.Add(msg);
            MessagesList.ScrollIntoView(msg);
            return msg;
        }

        private void SetBusy(bool busy)
        {
            SendButton.IsEnabled = !busy;
            StopButton.IsEnabled = busy;
            InputBox.IsEnabled = !busy;
        }

        private void StopClick(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }

        private void ClearClick(object sender, RoutedEventArgs e)
        {
            _streamFlushTimer.Stop();
            _streamBuffer.Clear();
            _streamDirty = false;
            _streamingMessage = null;
            Messages.Clear();
            _runner?.ClearHistory();
            StatusText.Text = "";
        }

        private void SettingsClick(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsWindow(_settings) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                ApplySettings();
                StatusText.Text = "Settings saved";
            }
        }

        private void OpenReportClick(object sender, RoutedEventArgs e)
        {
            var button = sender as FrameworkElement;
            var msg = button?.DataContext as ChatMessageViewModel;
            if (msg?.ReportPath == null || !File.Exists(msg.ReportPath))
                return;
            try
            {
                Process.Start(new ProcessStartInfo(msg.ReportPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusText.Text = "Cannot open report: " + ex.Message;
            }
        }
    }
}
