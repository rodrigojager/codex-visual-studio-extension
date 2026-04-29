using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace CodexVsix.UI;

public sealed class ChatMarkdownViewer : RichTextBox
{
    private bool _isRenderingDocument;

    public static readonly DependencyProperty MarkdownTextProperty = DependencyProperty.Register(
        nameof(MarkdownText),
        typeof(string),
        typeof(ChatMarkdownViewer),
        new PropertyMetadata(string.Empty, OnMarkdownTextChanged));

    public static readonly DependencyProperty LinkCommandProperty = DependencyProperty.Register(
        nameof(LinkCommand),
        typeof(ICommand),
        typeof(ChatMarkdownViewer),
        new PropertyMetadata(null, OnRenderContextChanged));

    public static readonly DependencyProperty WorkspaceRootProperty = DependencyProperty.Register(
        nameof(WorkspaceRoot),
        typeof(string),
        typeof(ChatMarkdownViewer),
        new PropertyMetadata(string.Empty, OnRenderContextChanged));

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
        RenderDocument();
    }

    public string MarkdownText
    {
        get => (string)GetValue(MarkdownTextProperty);
        set => SetValue(MarkdownTextProperty, value);
    }

    public ICommand? LinkCommand
    {
        get => (ICommand?)GetValue(LinkCommandProperty);
        set => SetValue(LinkCommandProperty, value);
    }

    public string WorkspaceRoot
    {
        get => (string)GetValue(WorkspaceRootProperty);
        set => SetValue(WorkspaceRootProperty, value);
    }

    private static void OnMarkdownTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ChatMarkdownViewer viewer)
        {
            return;
        }

        viewer.RenderDocument();
    }

    private static void OnRenderContextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatMarkdownViewer viewer)
        {
            viewer.RenderDocument();
        }
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (_isRenderingDocument)
        {
            return;
        }

        if (e.Property == ForegroundProperty)
        {
            // Rebuild the FlowDocument so markdown brushes follow VS theme/foreground updates.
            RenderDocument();
        }
    }

    private void RenderDocument()
    {
        _isRenderingDocument = true;
        try
        {
            Document = MarkdownRenderer.CreateDocument(
                MarkdownText ?? string.Empty,
                Foreground,
                new MarkdownRenderOptions(LinkCommand, WorkspaceRoot));
        }
        finally
        {
            _isRenderingDocument = false;
        }
    }
}
