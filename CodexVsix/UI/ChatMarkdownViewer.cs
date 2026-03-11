using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CodexVsix.UI;

public sealed class ChatMarkdownViewer : RichTextBox
{
    public static readonly DependencyProperty MarkdownTextProperty = DependencyProperty.Register(
        nameof(MarkdownText),
        typeof(string),
        typeof(ChatMarkdownViewer),
        new PropertyMetadata(string.Empty, OnMarkdownTextChanged));

    public ChatMarkdownViewer()
    {
        IsReadOnly = true;
        IsUndoEnabled = false;
        IsDocumentEnabled = true;
        Background = Brushes.Transparent;
        BorderBrush = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        Margin = new Thickness(0);
        AcceptsReturn = true;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        ContextMenuService.SetIsEnabled(this, true);
        Document = MarkdownRenderer.CreateDocument(string.Empty);
    }

    public string MarkdownText
    {
        get => (string)GetValue(MarkdownTextProperty);
        set => SetValue(MarkdownTextProperty, value);
    }

    private static void OnMarkdownTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ChatMarkdownViewer viewer)
        {
            return;
        }

        viewer.Document = MarkdownRenderer.CreateDocument(e.NewValue as string ?? string.Empty);
    }
}
