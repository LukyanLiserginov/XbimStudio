using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace XplorerPlugin.AiAgent.Chat
{
    /// <summary>Renders markdown text into a FlowDocument attached to a RichTextBox.</summary>
    public static class MarkdownAttach
    {
        public static readonly DependencyProperty MarkdownProperty =
            DependencyProperty.RegisterAttached(
                "Markdown", typeof(string), typeof(MarkdownAttach),
                new PropertyMetadata(null, OnMarkdownChanged));

        public static string GetMarkdown(DependencyObject obj)
        {
            return (string)obj.GetValue(MarkdownProperty);
        }

        public static void SetMarkdown(DependencyObject obj, string value)
        {
            obj.SetValue(MarkdownProperty, value);
        }

        private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var box = d as RichTextBox;
            if (box == null)
                return;
            var doc = box.Document;
            if (doc == null)
            {
                doc = new FlowDocument { PagePadding = new Thickness(0), FontSize = 13 };
                box.Document = doc;
            }
            MarkdownRenderer.RenderInto(doc, e.NewValue as string);
            box.InvalidateMeasure();
        }
    }
}
