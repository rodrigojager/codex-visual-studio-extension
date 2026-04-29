using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace CodexVsix.UI;

public sealed class ChatMarkdownViewer : RichTextBox
{
    private const double MinimumNaturalWidth = 24d;
    private const double NaturalWidthPadding = 20d;

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

    protected override Size MeasureOverride(Size constraint)
    {
        if (double.IsInfinity(constraint.Width) || constraint.Width <= 0)
        {
            return base.MeasureOverride(constraint);
        }

        var naturalWidth = CalculateNaturalWidth(constraint.Width);
        var targetWidth = Math.Max(MinimumNaturalWidth, Math.Min(constraint.Width, naturalWidth));
        var measured = base.MeasureOverride(new Size(targetWidth, constraint.Height));

        return new Size(targetWidth, measured.Height);
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

    private double CalculateNaturalWidth(double maxWidth)
    {
        var markdown = MarkdownText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return MinimumNaturalWidth;
        }

        if (ContainsWidthHungryMarkdown(markdown))
        {
            return maxWidth;
        }

        var maxLineWidth = 0d;
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            maxLineWidth = Math.Max(maxLineWidth, MeasureLineWidth(line));
            if (maxLineWidth + NaturalWidthPadding >= maxWidth)
            {
                return maxWidth;
            }
        }

        return Math.Ceiling(maxLineWidth + NaturalWidthPadding);
    }

    private static bool ContainsWidthHungryMarkdown(string markdown)
    {
        return markdown.IndexOf("```", StringComparison.Ordinal) >= 0;
    }

    private double MeasureLineWidth(string line)
    {
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var formattedText = new FormattedText(
            line,
            CultureInfo.CurrentUICulture,
            FlowDirection,
            typeface,
            FontSize,
            Foreground,
            pixelsPerDip);

        return formattedText.WidthIncludingTrailingWhitespace;
    }
}
