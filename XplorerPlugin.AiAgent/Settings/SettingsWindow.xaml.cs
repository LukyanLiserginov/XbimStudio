using System;
using System.Globalization;
using System.Windows;
using System.Windows.Forms;
using XplorerPlugin.AiAgent.Settings;

namespace XplorerPlugin.AiAgent
{
    /// <summary>Settings dialog for the AI agent plugin.</summary>
    public partial class SettingsWindow : Window
    {
        private readonly AgentSettings _settings;

        public SettingsWindow(AgentSettings settings)
        {
            _settings = settings;
            InitializeComponent();

            BaseUrlBox.Text = settings.BaseUrl ?? "";
            ApiKeyBox.Password = settings.ApiKey ?? "";
            ModelBox.Text = settings.Model ?? "";
            TemperatureBox.Text = settings.Temperature.ToString(CultureInfo.InvariantCulture);
            MaxIterBox.Text = settings.MaxToolIterations.ToString(CultureInfo.InvariantCulture);
            ReportFolderBox.Text = settings.EffectiveReportFolder;
            ExtraPromptBox.Text = settings.SystemPromptExtra ?? "";
        }

        private void BrowseClick(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select the folder where reports are saved";
                if (System.IO.Directory.Exists(ReportFolderBox.Text))
                    dialog.SelectedPath = ReportFolderBox.Text;
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    ReportFolderBox.Text = dialog.SelectedPath;
            }
        }

        private void SaveClick(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(TemperatureBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var temp))
                temp = 0.2;
            if (!int.TryParse(MaxIterBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iter))
                iter = 15;

            _settings.BaseUrl = BaseUrlBox.Text.Trim();
            _settings.ApiKey = ApiKeyBox.Password.Trim();
            _settings.Model = ModelBox.Text.Trim();
            _settings.Temperature = Math.Max(0, Math.Min(2, temp));
            _settings.MaxToolIterations = Math.Max(1, Math.Min(50, iter));
            _settings.ReportFolder = ReportFolderBox.Text.Trim();
            _settings.SystemPromptExtra = ExtraPromptBox.Text;
            try
            {
                _settings.Save();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, "Failed to save settings: " + ex.Message,
                    "AI Agent", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }
    }
}
