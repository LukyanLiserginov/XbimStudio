using System.ComponentModel;
using System.Windows.Media;

namespace XplorerPlugin.AiAgent.Chat
{
    public enum ChatRole
    {
        User,
        Assistant,
        ToolActivity,
        Error,
        Report
    }

    /// <summary>A single item displayed in the chat list.</summary>
    public class ChatMessageViewModel : INotifyPropertyChanged
    {
        private string _text;
        private bool _isStreaming;

        public ChatRole Role { get; set; }
        public string Author { get; set; }

        private string _reportPath;
        public string ReportPath
        {
            get => _reportPath;
            set
            {
                _reportPath = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReportPath)));
            }
        }

        public string Text
        {
            get => _text;
            set
            {
                _text = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
            }
        }

        public bool IsStreaming
        {
            get => _isStreaming;
            set
            {
                _isStreaming = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStreaming)));
            }
        }

        public Brush AuthorBrush
        {
            get
            {
                switch (Role)
                {
                    case ChatRole.User:
                        return Brushes.DarkSlateBlue;
                    case ChatRole.Error:
                        return Brushes.Firebrick;
                    case ChatRole.ToolActivity:
                        return Brushes.Gray;
                    case ChatRole.Report:
                        return Brushes.DarkGreen;
                    default:
                        return Brushes.Black;
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
