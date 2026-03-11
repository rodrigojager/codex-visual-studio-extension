using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CodexVsix.UI;

internal static class MarkdownRenderer
{
    private const string CopyIconPathData = "M384 336l0-208c0-35.3-28.7-64-64-64l-224 0c-35.3 0-64 28.7-64 64l0 208c0 35.3 28.7 64 64 64l224 0c35.3 0 64-28.7 64-64zM96 80l224 0c26.5 0 48 21.5 48 48l0 208c0 26.5-21.5 48-48 48l-224 0c-26.5 0-48-21.5-48-48l0-208c0-26.5 21.5-48 48-48zM448 96l0 208c0 44.2-35.8 80-80 80l-32 0 0-16 32 0c35.3 0 64-28.7 64-64l0-208c0-35.3-28.7-64-64-64l-224 0 0-16 224 0c44.2 0 80 35.8 80 80z";
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex BulletRegex = new(@"^\s*[-*+]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex OrderedRegex = new(@"^\s*\d+\.\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex MermaidEdgeRegex = new(@"(?<from>[A-Za-z0-9_.-]+)(?:\[(?<fromLabel>[^\]]+)\]|\((?<fromRound>[^)]+)\)|\{(?<fromBrace>[^}]+)\})?\s*(?<arrow>-{1,2}\.?-?>|={2,}>|--x)\s*(?:\|(?<edgeLabel>[^|]+)\|)?\s*(?<to>[A-Za-z0-9_.-]+)(?:\[(?<toLabel>[^\]]+)\]|\((?<toRound>[^)]+)\)|\{(?<toBrace>[^}]+)\})?", RegexOptions.Compiled);
    private static readonly Regex MermaidTextLabelEdgeRegex = new(@"(?<from>[A-Za-z0-9_.-]+)(?:\[(?<fromLabel>[^\]]+)\]|\((?<fromRound>[^)]+)\)|\{(?<fromBrace>[^}]+)\})?\s*--\s*(?<edgeLabel>[^-][^-]*?)\s*-->\s*(?<to>[A-Za-z0-9_.-]+)(?:\[(?<toLabel>[^\]]+)\]|\((?<toRound>[^)]+)\)|\{(?<toBrace>[^}]+)\})?", RegexOptions.Compiled);
    private static readonly Regex MermaidNodeRegex = new(@"^(?<id>[A-Za-z0-9_.-]+)(?:\[(?<label>[^\]]+)\]|\((?<round>[^)]+)\)|\{(?<brace>[^}]+)\})$", RegexOptions.Compiled);
    private static readonly Regex CodeTokenRegex = new(
        "(?<comment>//.*$)|(?<string>@?\"(?:[^\"]|\"\")*\"|'(?:\\\\.|[^'])')|(?<number>\\b\\d+(?:\\.\\d+)?\\b)|(?<keyword>\\b(?:using|namespace|class|struct|interface|enum|record|public|private|protected|internal|static|readonly|const|sealed|abstract|partial|async|await|return|new|var|void|int|string|bool|double|decimal|float|long|short|byte|char|object|null|true|false|if|else|switch|case|default|for|foreach|while|do|break|continue|try|catch|finally|throw|this|base|get|set|init|where|select|from|let|join|group|orderby|into)\\b)",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex ScriptTokenRegex = new(
        "(?<comment>//.*$|#.*$)|(?<string>`(?:\\\\.|[^`])*`|\"(?:\\\\.|[^\"])*\"|'(?:\\\\.|[^'])*')|(?<number>\\b\\d+(?:\\.\\d+)?\\b)|(?<keyword>\\b(?:const|let|var|function|return|class|interface|type|export|import|from|default|extends|implements|new|async|await|if|else|switch|case|for|while|do|break|continue|try|catch|finally|throw|true|false|null|undefined)\\b)",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex JsonTokenRegex = new(
        "(?<property>\"(?:\\\\.|[^\"])+\"\\s*:)|(?<string>:\\s*\"(?:\\\\.|[^\"])*\")|(?<number>:\\s*-?\\d+(?:\\.\\d+)?)|(?<keyword>:\\s*(?:true|false|null)\\b)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static FlowDocument CreateDocument(string markdown)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            Background = Brushes.Transparent,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            Foreground = Brushes.White
        };

        foreach (var block in ParseBlocks(markdown ?? string.Empty))
        {
            document.Blocks.Add(block);
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(CreateParagraph(string.Empty));
        }

        return document;
    }

    private static IEnumerable<Block> ParseBlocks(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var index = 0;

        while (index < lines.Length)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                index++;
                continue;
            }

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                yield return ParseCodeFence(lines, ref index);
                continue;
            }

            var headingMatch = HeadingRegex.Match(line);
            if (headingMatch.Success)
            {
                yield return CreateHeading(headingMatch.Groups[1].Value.Length, headingMatch.Groups[2].Value.Trim());
                index++;
                continue;
            }

            if (BulletRegex.IsMatch(line))
            {
                yield return ParseList(lines, ref index, ordered: false);
                continue;
            }

            if (OrderedRegex.IsMatch(line))
            {
                yield return ParseList(lines, ref index, ordered: true);
                continue;
            }

            yield return ParseParagraph(lines, ref index);
        }
    }

    private static Block ParseCodeFence(IReadOnlyList<string> lines, ref int index)
    {
        var fenceHeader = ParseFenceHeader(lines[index]);
        index++;

        var codeLines = new List<string>();
        if (!string.IsNullOrWhiteSpace(fenceHeader.InlineContent))
        {
            codeLines.Add(fenceHeader.InlineContent);
        }

        while (index < lines.Count && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            codeLines.Add(lines[index]);
            index++;
        }

        if (index < lines.Count)
        {
            index++;
        }

        var code = string.Join(Environment.NewLine, codeLines);
        return new BlockUIContainer(string.Equals(fenceHeader.Language, "mermaid", StringComparison.OrdinalIgnoreCase)
            ? CreateMermaidBlock(code)
            : CreateCodeBlock(code, fenceHeader.Language));
    }

    private static Block ParseList(IReadOnlyList<string> lines, ref int index, bool ordered)
    {
        var list = new List
        {
            MarkerStyle = ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(0, 6, 0, 10),
            Padding = new Thickness(12, 0, 0, 0)
        };

        while (index < lines.Count)
        {
            var line = lines[index];
            var match = ordered ? OrderedRegex.Match(line) : BulletRegex.Match(line);
            if (!match.Success)
            {
                break;
            }

            var paragraph = CreateParagraph(match.Groups[1].Value.Trim());
            paragraph.Margin = new Thickness(0);
            list.ListItems.Add(new ListItem(paragraph));
            index++;
        }

        return list;
    }

    private static Block ParseParagraph(IReadOnlyList<string> lines, ref int index)
    {
        var parts = new List<string>();
        while (index < lines.Count)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line) ||
                line.TrimStart().StartsWith("```", StringComparison.Ordinal) ||
                HeadingRegex.IsMatch(line) ||
                BulletRegex.IsMatch(line) ||
                OrderedRegex.IsMatch(line))
            {
                break;
            }

            parts.Add(line.Trim());
            index++;
        }

        return CreateParagraph(string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part))));
    }

    private static Paragraph CreateHeading(int level, string text)
    {
        var paragraph = CreateParagraph(text);
        paragraph.FontWeight = FontWeights.SemiBold;
        paragraph.Margin = new Thickness(0, level <= 2 ? 10 : 8, 0, 6);
        paragraph.FontSize = level switch
        {
            1 => 22,
            2 => 19,
            3 => 17,
            _ => 15
        };

        return paragraph;
    }

    private static Paragraph CreateParagraph(string text)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 4, 0, 8),
            LineHeight = 20
        };

        foreach (var inline in ParseInlines(text))
        {
            paragraph.Inlines.Add(inline);
        }

        return paragraph;
    }

    private static IEnumerable<Inline> ParseInlines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield return new Run(string.Empty);
            yield break;
        }

        var remaining = text;
        while (remaining.Length > 0)
        {
            var nextMatch = FindNextInlineMatch(remaining);
            if (nextMatch is null)
            {
                yield return new Run(remaining);
                yield break;
            }

            if (nextMatch.Index > 0)
            {
                yield return new Run(remaining.Substring(0, nextMatch.Index));
            }

            yield return nextMatch.ToInline();
            remaining = remaining.Substring(nextMatch.Index + nextMatch.Length);
        }
    }

    private static InlineMatch? FindNextInlineMatch(string text)
    {
        var candidates = new List<InlineMatch>();
        AddInlineMatch(candidates, text, @"`(?<content>[^`]+)`", match => CreateInlineCode(match.Groups["content"].Value));
        AddInlineMatch(candidates, text, @"\[(?<label>[^\]]+)\]\((?<url>[^)]+)\)", match => CreateHyperlink(match.Groups["label"].Value, match.Groups["url"].Value));
        AddInlineMatch(candidates, text, @"\*\*(?<content>[^*]+)\*\*", match => new Bold(new Run(match.Groups["content"].Value)));
        AddInlineMatch(candidates, text, @"\*(?<content>[^*]+)\*", match => new Italic(new Run(match.Groups["content"].Value)));

        return candidates
            .OrderBy(candidate => candidate.Index)
            .ThenBy(candidate => candidate.Length)
            .FirstOrDefault();
    }

    private static void AddInlineMatch(List<InlineMatch> matches, string text, string pattern, Func<Match, Inline> factory)
    {
        var match = Regex.Match(text, pattern);
        if (match.Success)
        {
            matches.Add(new InlineMatch(match.Index, match.Length, () => factory(match)));
        }
    }

    private static Inline CreateInlineCode(string code)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(1, 0, 1, 0),
            Child = new TextBlock
            {
                Text = code,
                FontFamily = new FontFamily("Consolas"),
                Foreground = Brushes.White
            }
        };

        return new InlineUIContainer(border)
        {
            BaselineAlignment = BaselineAlignment.Center
        };
    }

    private static Inline CreateHyperlink(string label, string url)
    {
        var hyperlink = new Hyperlink(new Run(label))
        {
            Foreground = new SolidColorBrush(Color.FromRgb(125, 193, 255)),
            Cursor = System.Windows.Input.Cursors.Hand
        };

        hyperlink.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        };

        return hyperlink;
    }

    private static FrameworkElement CreateCodeBlock(string code, string language)
    {
        var host = new StackPanel();
        host.Children.Add(CreateBlockToolbar(GetLanguageDisplayName(language), code, null));
        host.Children.Add(CreateCodeViewer(code, language));
        return CreateBlockBorder(host);
    }

    private static FrameworkElement CreateMermaidBlock(string code)
    {
        var preview = CreateMermaidPreview(code);
        var codeView = CreateCodeViewer(code, "mermaid");
        codeView.Visibility = Visibility.Collapsed;

        var host = new StackPanel();
        host.Children.Add(CreateBlockToolbar("Mermaid", code, CreateMermaidViewSwitch(preview, codeView)));
        host.Children.Add(preview);
        host.Children.Add(codeView);
        return CreateBlockBorder(host);
    }

    private static FrameworkElement CreateBlockBorder(FrameworkElement child)
    {
        return new Border
        {
            Margin = new Thickness(0, 8, 0, 12),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = child
        };
    }

    private static FrameworkElement CreateBlockToolbar(string title, string contentToCopy, FrameworkElement? leadingControl)
    {
        var panel = new DockPanel
        {
            Margin = new Thickness(0, 0, 0, 10),
            LastChildFill = false
        };

        var actionsHost = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        if (leadingControl is not null)
        {
            actionsHost.Children.Add(leadingControl);
        }

        var copyButton = CreateIconButton(CopyIconPathData, "Copiar");
        copyButton.Click += (_, _) => Clipboard.SetText(contentToCopy ?? string.Empty);
        actionsHost.Children.Add(copyButton);

        DockPanel.SetDock(actionsHost, Dock.Right);
        panel.Children.Add(actionsHost);
        panel.Children.Add(CreateLanguageBadge(title));

        return panel;
    }

    private static FrameworkElement CreateMermaidViewSwitch(FrameworkElement preview, FrameworkElement codeView)
    {
        var stateLabel = new TextBlock
        {
            Text = "Diagrama",
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold
        };

        var toggle = new ToggleButton
        {
            IsChecked = true,
            Width = 40,
            Height = 22,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };

        var rootFactory = new FrameworkElementFactory(typeof(Grid));

        var trackFactory = new FrameworkElementFactory(typeof(Border));
        trackFactory.Name = "Track";
        trackFactory.SetValue(FrameworkElement.WidthProperty, 40d);
        trackFactory.SetValue(FrameworkElement.HeightProperty, 22d);
        trackFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
        trackFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(74, 155, 230)));
        trackFactory.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)));
        trackFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));

        var thumbFactory = new FrameworkElementFactory(typeof(Border));
        thumbFactory.Name = "Thumb";
        thumbFactory.SetValue(FrameworkElement.WidthProperty, 16d);
        thumbFactory.SetValue(FrameworkElement.HeightProperty, 16d);
        thumbFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 3, 0));
        thumbFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        thumbFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        thumbFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(999));
        thumbFactory.SetValue(Border.BackgroundProperty, Brushes.White);
        thumbFactory.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(24, 0, 0, 0)));
        thumbFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));

        rootFactory.AppendChild(trackFactory);
        rootFactory.AppendChild(thumbFactory);

        var template = new ControlTemplate(typeof(ToggleButton))
        {
            VisualTree = rootFactory
        };

        var uncheckedTrigger = new Trigger
        {
            Property = ToggleButton.IsCheckedProperty,
            Value = false
        };
        uncheckedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)), "Track"));
        uncheckedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), "Track"));
        uncheckedTrigger.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left, "Thumb"));
        uncheckedTrigger.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(3, 0, 0, 0), "Thumb"));

        var hoverTrigger = new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true
        };
        hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), "Track"));

        template.Triggers.Add(uncheckedTrigger);
        template.Triggers.Add(hoverTrigger);
        toggle.Template = template;

        toggle.Checked += (_, _) =>
        {
            stateLabel.Text = "Diagrama";
            preview.Visibility = Visibility.Visible;
            codeView.Visibility = Visibility.Collapsed;
        };

        toggle.Unchecked += (_, _) =>
        {
            stateLabel.Text = "Código";
            preview.Visibility = Visibility.Collapsed;
            codeView.Visibility = Visibility.Visible;
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Children =
            {
                stateLabel,
                toggle
            }
        };
    }

    private static Border CreateLanguageBadge(string title)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = title,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255))
            }
        };
    }

    private static Button CreateIconButton(string pathData, string toolTip)
    {
        var button = new Button
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(6),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = toolTip,
            Content = new Viewbox
            {
                Width = 12,
                Height = 12,
                Child = new Path
                {
                    Data = Geometry.Parse(pathData),
                    Fill = Brushes.White,
                    Stretch = Stretch.Uniform
                }
            }
        };

        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.Name = "Chrome";
        borderFactory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        borderFactory.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(0));
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(999));

        var presenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        presenterFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenterFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        borderFactory.AppendChild(presenterFactory);

        var template = new ControlTemplate(typeof(Button))
        {
            VisualTree = borderFactory
        };

        var hoverTrigger = new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true
        };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)), "Chrome"));
        hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), "Chrome"));
        hoverTrigger.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1), "Chrome"));

        var pressedTrigger = new Trigger
        {
            Property = ButtonBase.IsPressedProperty,
            Value = true
        };
        pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(44, 255, 255, 255)), "Chrome"));
        pressedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(56, 255, 255, 255)), "Chrome"));
        pressedTrigger.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1), "Chrome"));

        template.Triggers.Add(hoverTrigger);
        template.Triggers.Add(pressedTrigger);
        button.Template = template;

        return button;
    }

    private static FrameworkElement CreateCodeViewer(string code, string language)
    {
        return new RichTextBox
        {
            IsReadOnly = true,
            IsDocumentEnabled = true,
            IsUndoEnabled = false,
            AcceptsReturn = true,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Consolas"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Document = CreateCodeDocument(code, language)
        };
    }

    private static FlowDocument CreateCodeDocument(string code, string language)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            Background = Brushes.Transparent,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Foreground = Brushes.White
        };

        var normalizedLanguage = (language ?? string.Empty).Trim().ToLowerInvariant();
        var lines = (code ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var line in lines)
        {
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0),
                LineHeight = 18
            };

            foreach (var inline in CreateCodeInlines(line, normalizedLanguage))
            {
                paragraph.Inlines.Add(inline);
            }

            if (paragraph.Inlines.Count == 0)
            {
                paragraph.Inlines.Add(new Run(string.Empty));
            }

            document.Blocks.Add(paragraph);
        }

        return document;
    }

    private static IEnumerable<Inline> CreateCodeInlines(string line, string language)
    {
        var regex = SelectSyntaxRegex(language);
        if (regex is null || string.IsNullOrEmpty(line))
        {
            yield return new Run(line ?? string.Empty);
            yield break;
        }

        var index = 0;
        foreach (Match match in regex.Matches(line))
        {
            if (!match.Success)
            {
                continue;
            }

            if (match.Index > index)
            {
                yield return new Run(line.Substring(index, match.Index - index));
            }

            yield return CreateHighlightedRun(match);
            index = match.Index + match.Length;
        }

        if (index < line.Length)
        {
            yield return new Run(line.Substring(index));
        }
    }

    private static Regex? SelectSyntaxRegex(string language)
    {
        return language switch
        {
            "csharp" or "cs" or "c#" or "java" or "kotlin" => CodeTokenRegex,
            "javascript" or "js" or "typescript" or "ts" => ScriptTokenRegex,
            "json" => JsonTokenRegex,
            _ => null
        };
    }

    private static Run CreateHighlightedRun(Match match)
    {
        var text = match.Value;
        if (match.Groups["comment"].Success)
        {
            return CreateRun(text, Color.FromRgb(106, 153, 85));
        }

        if (match.Groups["string"].Success)
        {
            return CreateRun(text, Color.FromRgb(206, 145, 120));
        }

        if (match.Groups["number"].Success)
        {
            return CreateRun(text, Color.FromRgb(181, 206, 168));
        }

        if (match.Groups["property"].Success)
        {
            return CreateRun(text, Color.FromRgb(156, 220, 254));
        }

        if (match.Groups["keyword"].Success)
        {
            return CreateRun(text, Color.FromRgb(86, 156, 214), FontWeights.SemiBold);
        }

        return CreateRun(text, Colors.White);
    }

    private static Run CreateRun(string text, Color color, FontWeight? weight = null)
    {
        var run = new Run(text)
        {
            Foreground = new SolidColorBrush(color)
        };

        if (weight is not null)
        {
            run.FontWeight = weight.Value;
        }

        return run;
    }

#pragma warning disable CS0162
    private static FrameworkElement CreateMermaidPreview(string code)
    {
        var diagram = ParseMermaidDiagram(code);
        if (diagram.Nodes.Count == 0)
        {
            var fallbackText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255))
            };
            fallbackText.Text = "Não foi possível montar a prévia deste Mermaid. Use o toggle para alternar entre diagrama e código.";
            fallbackText.Text = "\u004e\u00e3o foi poss\u00edvel montar a pr\u00e9via deste Mermaid. Use o toggle para alternar entre diagrama e c\u00f3digo.";
            return fallbackText;

            return new TextBlock
            {
                Text = "Não foi possível montar a prévia deste Mermaid. Use o toggle para alternar entre diagrama e código.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255))
            };
        }

        LayoutMermaidDiagram(diagram);

        var canvas = new Canvas
        {
            Width = diagram.CanvasWidth,
            Height = diagram.CanvasHeight,
            Background = Brushes.Transparent
        };

        foreach (var subgraph in diagram.Subgraphs.OrderBy(group => group.Depth).ThenBy(group => group.Id, StringComparer.Ordinal))
        {
            var groupElement = CreateMermaidSubgraphElement(subgraph);
            Canvas.SetLeft(groupElement, subgraph.X);
            Canvas.SetTop(groupElement, subgraph.Y);
            Panel.SetZIndex(groupElement, 0);
            canvas.Children.Add(groupElement);
        }

        foreach (var edge in diagram.Edges)
        {
            AddMermaidEdgeVisual(canvas, edge);
        }

        foreach (var node in diagram.Nodes.Values)
        {
            var element = CreateMermaidNodeElement(node);
            Canvas.SetLeft(element, node.X);
            Canvas.SetTop(element, node.Y);
            Panel.SetZIndex(element, 10);
            canvas.Children.Add(element);
        }

        return new Border
        {
            Margin = new Thickness(0, 2, 0, 0),
            Padding = new Thickness(14),
            Background = CreateMermaidCanvasBackground(),
            CornerRadius = new CornerRadius(10),
            Child = new Viewbox
            {
                IsHitTestVisible = false,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                Child = canvas
            }
        };
    }

    private static MermaidDiagram ParseMermaidDiagram(string code)
    {
        var diagram = new MermaidDiagram();
        var subgraphStack = new Stack<MermaidSubgraph>();

        foreach (var rawLine in (code ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            var normalizedLine = NormalizeMermaidEdgeLine(rawLine);
            foreach (var statement in EnumerateMermaidStatements(normalizedLine))
            {
                if (string.IsNullOrWhiteSpace(statement) || statement.StartsWith("%%", StringComparison.Ordinal))
                {
                    continue;
                }

                if (TryHandleSubgraphStatement(diagram, statement, subgraphStack))
                {
                    continue;
                }

                if (TryHandleClassDefinition(diagram, statement))
                {
                    continue;
                }

                if (TryHandleClassAssignment(diagram, statement))
                {
                    continue;
                }

                if (TryHandleNodeStyle(diagram, statement))
                {
                    continue;
                }

                if (TryHandleLinkStyle(diagram, statement))
                {
                    continue;
                }

                if (TryHandleCompoundEdge(diagram, statement, subgraphStack))
                {
                    continue;
                }

                var nodeMatch = MermaidNodeRegex.Match(statement);
                if (nodeMatch.Success)
                {
                    GetOrCreateMermaidNode(
                        diagram,
                        nodeMatch.Groups["id"].Value,
                        nodeMatch.Groups["label"].Value,
                        nodeMatch.Groups["round"].Value,
                        nodeMatch.Groups["brace"].Value,
                        subgraphStack);
                    continue;
                }

                var textLabelMatch = MermaidTextLabelEdgeRegex.Match(statement);
                if (textLabelMatch.Success)
                {
                    var fromNode = GetOrCreateMermaidNode(
                        diagram,
                        textLabelMatch.Groups["from"].Value,
                        textLabelMatch.Groups["fromLabel"].Value,
                        textLabelMatch.Groups["fromRound"].Value,
                        textLabelMatch.Groups["fromBrace"].Value,
                        subgraphStack);
                    var toNode = GetOrCreateMermaidNode(
                        diagram,
                        textLabelMatch.Groups["to"].Value,
                        textLabelMatch.Groups["toLabel"].Value,
                        textLabelMatch.Groups["toRound"].Value,
                        textLabelMatch.Groups["toBrace"].Value,
                        subgraphStack);

                    diagram.Edges.Add(CreateMermaidEdge(diagram, fromNode, toNode, "-->", NormalizeMermaidText(textLabelMatch.Groups["edgeLabel"].Value)));
                    continue;
                }

                var match = MermaidEdgeRegex.Match(statement);
                if (!match.Success)
                {
                    continue;
                }

                var from = GetOrCreateMermaidNode(
                    diagram,
                    match.Groups["from"].Value,
                    match.Groups["fromLabel"].Value,
                    match.Groups["fromRound"].Value,
                    match.Groups["fromBrace"].Value,
                    subgraphStack);
                var to = GetOrCreateMermaidNode(
                    diagram,
                    match.Groups["to"].Value,
                    match.Groups["toLabel"].Value,
                    match.Groups["toRound"].Value,
                    match.Groups["toBrace"].Value,
                    subgraphStack);

                diagram.Edges.Add(CreateMermaidEdge(diagram, from, to, match.Groups["arrow"].Value, NormalizeMermaidText(match.Groups["edgeLabel"].Value)));
            }
        }

        ApplyNodeClasses(diagram);

        return diagram;
    }

    private static MermaidNode GetOrCreateMermaidNode(
        MermaidDiagram diagram,
        string id,
        string squareLabel,
        string roundedLabel,
        string diamondLabel,
        IEnumerable<MermaidSubgraph>? activeSubgraphs = null)
    {
        if (!diagram.Nodes.TryGetValue(id, out var node))
        {
            var label = NormalizeMermaidText(SelectNodeLabel(id, squareLabel, roundedLabel, diamondLabel));
            node = new MermaidNode(id, label, GetNodeShape(squareLabel, roundedLabel, diamondLabel));
            diagram.Nodes.Add(id, node);
        }
        else
        {
            var label = NormalizeMermaidText(SelectNodeLabel(id, squareLabel, roundedLabel, diamondLabel));
            if (!string.IsNullOrWhiteSpace(label) && string.Equals(node.Label, node.Id, StringComparison.Ordinal))
            {
                node.Label = label;
            }

            var shape = GetNodeShape(squareLabel, roundedLabel, diamondLabel);
            if (shape != MermaidNodeShape.Rectangle)
            {
                node.Shape = shape;
            }
        }

        if (activeSubgraphs is not null)
        {
            foreach (var subgraph in activeSubgraphs)
            {
                subgraph.NodeIds.Add(node.Id);
            }
        }

        return node;
    }

    private static IEnumerable<string> EnumerateMermaidStatements(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            yield break;
        }

        var start = 0;
        var bracketDepth = 0;
        var parenDepth = 0;
        var braceDepth = 0;
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];
            switch (current)
            {
                case '"':
                    inQuotes = !inQuotes;
                    break;
                case '[' when !inQuotes:
                    bracketDepth++;
                    break;
                case ']' when !inQuotes && bracketDepth > 0:
                    bracketDepth--;
                    break;
                case '(' when !inQuotes:
                    parenDepth++;
                    break;
                case ')' when !inQuotes && parenDepth > 0:
                    parenDepth--;
                    break;
                case '{' when !inQuotes:
                    braceDepth++;
                    break;
                case '}' when !inQuotes && braceDepth > 0:
                    braceDepth--;
                    break;
                case ';' when !inQuotes && bracketDepth == 0 && parenDepth == 0 && braceDepth == 0:
                    var statement = line.Substring(start, index - start).Trim();
                    if (!string.IsNullOrWhiteSpace(statement))
                    {
                        yield return statement;
                    }

                    start = index + 1;
                    break;
            }
        }

        var trailing = line.Substring(start).Trim();
        if (!string.IsNullOrWhiteSpace(trailing))
        {
            yield return trailing;
        }
    }

    private static bool TryHandleSubgraphStatement(MermaidDiagram diagram, string statement, Stack<MermaidSubgraph> subgraphStack)
    {
        if (string.Equals(statement, "end", StringComparison.OrdinalIgnoreCase))
        {
            if (subgraphStack.Count > 0)
            {
                subgraphStack.Pop();
            }

            return true;
        }

        const string prefix = "subgraph ";
        if (!statement.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var descriptor = statement.Substring(prefix.Length).Trim();
        if (string.IsNullOrWhiteSpace(descriptor))
        {
            return true;
        }

        var nodeMatch = MermaidNodeRegex.Match(descriptor);
        var id = nodeMatch.Success ? nodeMatch.Groups["id"].Value : $"subgraph-{diagram.Subgraphs.Count + 1}";
        var label = nodeMatch.Success
            ? SelectNodeLabel(id, nodeMatch.Groups["label"].Value, nodeMatch.Groups["round"].Value, nodeMatch.Groups["brace"].Value)
            : NormalizeMermaidText(descriptor);

        var subgraph = new MermaidSubgraph(id, string.IsNullOrWhiteSpace(label) ? id : label)
        {
            Depth = subgraphStack.Count
        };
        if (subgraphStack.Count > 0)
        {
            subgraph.ParentId = subgraphStack.Peek().Id;
        }

        diagram.Subgraphs.Add(subgraph);
        subgraphStack.Push(subgraph);
        return true;
    }

    private static bool TryHandleClassDefinition(MermaidDiagram diagram, string statement)
    {
        const string prefix = "classDef ";
        if (!statement.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var content = statement.Substring(prefix.Length).Trim();
        var separator = content.IndexOf(' ');
        if (separator <= 0)
        {
            return true;
        }

        var className = content.Substring(0, separator).Trim();
        var style = ParseMermaidStyle(content.Substring(separator + 1));
        diagram.ClassDefinitions[className] = style;
        return true;
    }

    private static bool TryHandleClassAssignment(MermaidDiagram diagram, string statement)
    {
        const string prefix = "class ";
        if (!statement.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var content = statement.Substring(prefix.Length).Trim();
        var separator = content.IndexOf(' ');
        if (separator <= 0)
        {
            return true;
        }

        var targets = content.Substring(0, separator)
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim());
        var classes = content.Substring(separator + 1)
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        foreach (var target in targets)
        {
            var node = GetOrCreateMermaidNode(diagram, target, string.Empty, string.Empty, string.Empty);
            foreach (var className in classes)
            {
                if (!node.ClassNames.Any(existing => string.Equals(existing, className, StringComparison.Ordinal)))
                {
                    node.ClassNames.Add(className);
                }
            }
        }

        return true;
    }

    private static bool TryHandleNodeStyle(MermaidDiagram diagram, string statement)
    {
        const string prefix = "style ";
        if (!statement.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var content = statement.Substring(prefix.Length).Trim();
        var separator = content.IndexOf(' ');
        if (separator <= 0)
        {
            return true;
        }

        var target = content.Substring(0, separator).Trim();
        var style = ParseMermaidStyle(content.Substring(separator + 1));

        var node = GetOrCreateMermaidNode(diagram, target, string.Empty, string.Empty, string.Empty);
        node.InlineStyle = MergeMermaidStyle(node.InlineStyle, style);
        return true;
    }

    private static bool TryHandleLinkStyle(MermaidDiagram diagram, string statement)
    {
        const string prefix = "linkStyle ";
        if (!statement.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var content = statement.Substring(prefix.Length).Trim();
        var separator = content.IndexOf(' ');
        if (separator <= 0)
        {
            return true;
        }

        var target = content.Substring(0, separator).Trim();
        var style = ParseMermaidStyle(content.Substring(separator + 1));
        if (string.Equals(target, "default", StringComparison.OrdinalIgnoreCase))
        {
            diagram.DefaultLinkStyle = MergeMermaidStyle(diagram.DefaultLinkStyle, style);
            foreach (var edge in diagram.Edges)
            {
                edge.InlineStyle = MergeMermaidStyle(edge.InlineStyle, style);
            }

            return true;
        }

        foreach (var indexToken in target.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(indexToken.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                continue;
            }

            if (index >= 0 && index < diagram.Edges.Count)
            {
                diagram.Edges[index].InlineStyle = MergeMermaidStyle(diagram.Edges[index].InlineStyle, style);
            }
        }

        return true;
    }

    private static bool TryHandleCompoundEdge(MermaidDiagram diagram, string statement, IEnumerable<MermaidSubgraph> activeSubgraphs)
    {
        var arrowInfo = FindMermaidArrowToken(statement);
        if (arrowInfo is null)
        {
            return false;
        }

        var (arrow, arrowIndex) = arrowInfo.Value;
        var leftPart = statement.Substring(0, arrowIndex).Trim();
        var rightPart = statement.Substring(arrowIndex + arrow.Length).Trim();
        if (leftPart.IndexOf('&') < 0 && rightPart.IndexOf('&') < 0)
        {
            return false;
        }

        var sources = ParseCompoundMermaidNodeList(diagram, leftPart, activeSubgraphs);
        var targets = ParseCompoundMermaidNodeList(diagram, rightPart, activeSubgraphs);
        if (sources.Count == 0 || targets.Count == 0)
        {
            return false;
        }

        foreach (var source in sources)
        {
            foreach (var target in targets)
            {
                diagram.Edges.Add(CreateMermaidEdge(diagram, source, target, arrow, string.Empty));
            }
        }

        return true;
    }

    private static List<MermaidNode> ParseCompoundMermaidNodeList(MermaidDiagram diagram, string segment, IEnumerable<MermaidSubgraph> activeSubgraphs)
    {
        var nodes = new List<MermaidNode>();
        foreach (var token in segment.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = token.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            var nodeMatch = MermaidNodeRegex.Match(trimmed);
            if (nodeMatch.Success)
            {
                nodes.Add(GetOrCreateMermaidNode(
                    diagram,
                    nodeMatch.Groups["id"].Value,
                    nodeMatch.Groups["label"].Value,
                    nodeMatch.Groups["round"].Value,
                    nodeMatch.Groups["brace"].Value,
                    activeSubgraphs));
                continue;
            }

            nodes.Add(GetOrCreateMermaidNode(diagram, trimmed, string.Empty, string.Empty, string.Empty, activeSubgraphs));
        }

        return nodes;
    }

    private static (string Arrow, int Index)? FindMermaidArrowToken(string statement)
    {
        foreach (var arrow in new[] { "--.->", "-.->", "-->", "->", "==>", "--x" })
        {
            var index = statement.IndexOf(arrow, StringComparison.Ordinal);
            if (index >= 0)
            {
                return (arrow, index);
            }
        }

        return null;
    }

    private static MermaidEdge CreateMermaidEdge(MermaidDiagram diagram, MermaidNode from, MermaidNode to, string arrow, string label)
    {
        return new MermaidEdge(from, to, arrow, label)
        {
            InlineStyle = diagram.DefaultLinkStyle?.Clone()
        };
    }

    private static void ApplyNodeClasses(MermaidDiagram diagram)
    {
        foreach (var node in diagram.Nodes.Values)
        {
            MermaidStyle? effectiveStyle = null;
            foreach (var className in node.ClassNames)
            {
                if (diagram.ClassDefinitions.TryGetValue(className, out var classStyle))
                {
                    effectiveStyle = MergeMermaidStyle(effectiveStyle, classStyle);
                }
            }

            node.ClassStyle = effectiveStyle;
        }
    }

    private static MermaidStyle ParseMermaidStyle(string styleText)
    {
        var style = new MermaidStyle();
        foreach (var entry in styleText.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = entry.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var name = entry.Substring(0, separator).Trim().ToLowerInvariant();
            var value = entry.Substring(separator + 1).Trim();
            switch (name)
            {
                case "fill":
                    style.Fill = value;
                    break;
                case "stroke":
                    style.Stroke = value;
                    break;
                case "color":
                    style.Text = value;
                    break;
                case "stroke-width":
                    if (TryParseCssDouble(value, out var strokeWidth))
                    {
                        style.StrokeThickness = strokeWidth;
                    }
                    break;
                case "stroke-dasharray":
                    style.StrokeDashArray = value;
                    break;
            }
        }

        return style;
    }

    private static MermaidStyle MergeMermaidStyle(MermaidStyle? baseStyle, MermaidStyle? overlay)
    {
        if (baseStyle is null && overlay is null)
        {
            return new MermaidStyle();
        }

        if (baseStyle is null)
        {
            return overlay!.Clone();
        }

        if (overlay is null)
        {
            return baseStyle.Clone();
        }

        return new MermaidStyle
        {
            Fill = overlay.Fill ?? baseStyle.Fill,
            Stroke = overlay.Stroke ?? baseStyle.Stroke,
            Text = overlay.Text ?? baseStyle.Text,
            StrokeThickness = overlay.StrokeThickness ?? baseStyle.StrokeThickness,
            StrokeDashArray = overlay.StrokeDashArray ?? baseStyle.StrokeDashArray
        };
    }

    private static bool TryParseCssDouble(string value, out double result)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(0, normalized.Length - 2);
        }

        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static string SelectNodeLabel(string id, params string[] labels)
    {
        foreach (var label in labels)
        {
            var normalized = NormalizeMermaidText(label);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return NormalizeMermaidText(id);
    }

    private static MermaidNodeShape GetNodeShape(string squareLabel, string roundedLabel, string diamondLabel)
    {
        if (!string.IsNullOrWhiteSpace(diamondLabel))
        {
            return MermaidNodeShape.Diamond;
        }

        if (!string.IsNullOrWhiteSpace(roundedLabel))
        {
            return MermaidNodeShape.Rounded;
        }

        return MermaidNodeShape.Rectangle;
    }

    private static string NormalizeMermaidText(string value)
    {
        var normalized = (value ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        normalized = ConvertMermaidIcons(normalized);
        normalized = normalized.Replace("<br/>", "\n").Replace("<br>", "\n");
        return normalized.Trim();
    }

    private static string ConvertMermaidIcons(string value)
    {
        return Regex.Replace(value ?? string.Empty, @"\bfa:fa-([a-z0-9-]+)\b\s*", match =>
        {
            return match.Groups[1].Value.ToLowerInvariant() switch
            {
                "car" => "\ud83d\ude97 ",
                "laptop" => "\ud83d\udcbb ",
                "phone" or "mobile" => "\ud83d\udcf1 ",
                "money-bill" or "money-check-dollar" or "dollar-sign" => "\ud83d\udcb5 ",
                _ => string.Empty
            };
        }, RegexOptions.IgnoreCase);

        return Regex.Replace(value ?? string.Empty, @"\bfa:fa-([a-z0-9-]+)\b\s*", match =>
        {
            return match.Groups[1].Value.ToLowerInvariant() switch
            {
                "car" => "🚗 ",
                "laptop" => "💻 ",
                "phone" or "mobile" => "📱 ",
                "money-bill" or "money-check-dollar" or "dollar-sign" => "💵 ",
                _ => string.Empty
            };
        }, RegexOptions.IgnoreCase);
    }
#pragma warning restore CS0162

    private static void LayoutMermaidDiagram(MermaidDiagram diagram)
    {
        var incomingCounts = diagram.Nodes.Keys.ToDictionary(key => key, _ => 0);
        foreach (var edge in diagram.Edges)
        {
            incomingCounts[edge.To.Id]++;
        }

        var roots = diagram.Nodes.Values
            .Where(node => incomingCounts[node.Id] == 0)
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .ToList();

        if (roots.Count == 0)
        {
            roots.Add(diagram.Nodes.Values.OrderBy(node => node.Id, StringComparer.Ordinal).First());
        }

        var outgoingByNode = diagram.Edges
            .GroupBy(edge => edge.From.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.To).Distinct().ToList(), StringComparer.Ordinal);

        var queue = new Queue<MermaidNode>(roots);
        var visited = new HashSet<string>(roots.Select(node => node.Id), StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!outgoingByNode.TryGetValue(current.Id, out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                if (!visited.Add(child.Id))
                {
                    continue;
                }

                child.Level = current.Level + 1;
                queue.Enqueue(child);
            }
        }

        foreach (var node in diagram.Nodes.Values)
        {
            MeasureMermaidNode(node);
        }

        var levelGroups = diagram.Nodes.Values
            .GroupBy(node => node.Level)
            .OrderBy(group => group.Key)
            .ToList();

        const double minCanvasWidth = 680;
        const double horizontalGap = 104;
        var widestRow = levelGroups
            .Select(group => group.Sum(node => node.Width) + Math.Max(0, group.Count() - 1) * horizontalGap)
            .DefaultIfEmpty(minCanvasWidth)
            .Max();

        diagram.CanvasWidth = Math.Max(minCanvasWidth, widestRow + 80);

        const double topPadding = 28;
        const double minVerticalGap = 42;
        const double bottomPadding = 44;

        var currentY = topPadding;
        var deepestBottom = topPadding;
        for (var index = 0; index < levelGroups.Count; index++)
        {
            var group = levelGroups[index];
            var nodes = group
                .OrderBy(node => GetMermaidNodeOrder(diagram, node))
                .ThenBy(node => node.Id, StringComparer.Ordinal)
                .ToList();
            var rowHeight = nodes.Max(node => node.Height);
            var rowWidth = nodes.Sum(node => node.Width) + Math.Max(0, nodes.Count - 1) * horizontalGap;
            var x = (diagram.CanvasWidth - rowWidth) / 2d;

            foreach (var node in nodes)
            {
                node.X = x;
                node.Y = currentY + ((rowHeight - node.Height) / 2d);
                x += node.Width + horizontalGap;
            }

            deepestBottom = Math.Max(deepestBottom, currentY + rowHeight);
            if (index < levelGroups.Count - 1)
            {
                currentY += rowHeight + GetMermaidInterLevelGap(diagram, nodes, minVerticalGap);
            }
        }

        diagram.CanvasHeight = deepestBottom + bottomPadding;
        MeasureMermaidSubgraphs(diagram);
    }

    private static void MeasureMermaidSubgraphs(MermaidDiagram diagram)
    {
        foreach (var subgraph in diagram.Subgraphs.OrderByDescending(group => group.Depth))
        {
            var memberNodes = subgraph.NodeIds
                .Select(id => diagram.Nodes.TryGetValue(id, out var node) ? node : null)
                .Where(node => node is not null)
                .Cast<MermaidNode>()
                .ToList();

            if (memberNodes.Count == 0)
            {
                continue;
            }

            const double horizontalPadding = 20;
            const double topPadding = 34;
            const double bottomPadding = 18;

            var left = memberNodes.Min(node => node.X) - horizontalPadding;
            var right = memberNodes.Max(node => node.X + node.Width) + horizontalPadding;
            var top = memberNodes.Min(node => node.Y) - topPadding;
            var bottom = memberNodes.Max(node => node.Y + node.Height) + bottomPadding;

            subgraph.X = left;
            subgraph.Y = top;
            subgraph.Width = right - left;
            subgraph.Height = bottom - top;

            diagram.CanvasWidth = Math.Max(diagram.CanvasWidth, right + horizontalPadding);
            diagram.CanvasHeight = Math.Max(diagram.CanvasHeight, bottom + bottomPadding);
        }
    }

    private static double GetMermaidInterLevelGap(MermaidDiagram diagram, IReadOnlyCollection<MermaidNode> rowNodes, double minVerticalGap)
    {
        var rowIds = new HashSet<string>(rowNodes.Select(node => node.Id), StringComparer.Ordinal);
        var hasOutgoingLabel = diagram.Edges.Any(edge => rowIds.Contains(edge.From.Id) && !string.IsNullOrWhiteSpace(edge.Label));
        var hasDiamond = rowNodes.Any(node => node.Shape == MermaidNodeShape.Diamond);
        var extraGap = 0d;

        if (hasOutgoingLabel)
        {
            extraGap += 12d;
        }

        if (hasDiamond)
        {
            extraGap += 6d;
        }

        return minVerticalGap + extraGap;
    }

    private static double GetMermaidNodeOrder(MermaidDiagram diagram, MermaidNode node)
    {
        var parents = diagram.Edges
            .Where(edge => ReferenceEquals(edge.To, node))
            .Select(edge => edge.From)
            .Distinct()
            .OrderBy(parent => parent.Level)
            .ThenBy(parent => parent.Id, StringComparer.Ordinal)
            .ToList();

        if (parents.Count == 0)
        {
            return node.Id[0];
        }

        return parents.Average(parent => (parent.X > 0 ? parent.X : parent.Id[0]));
    }

    private static void MeasureMermaidNode(MermaidNode node)
    {
        var textLength = Math.Max(4, node.Label.Length);
        var baseWidth = Math.Min(256, Math.Max(126, textLength * 8 + 44));

        switch (node.Shape)
        {
            case MermaidNodeShape.Diamond:
                node.Width = Math.Max(176, baseWidth);
                node.Height = Math.Max(112, EstimateMermaidNodeHeight(node.Label, node.Width * 0.64, 76));
                break;
            case MermaidNodeShape.Rounded:
                node.Width = Math.Max(140, baseWidth);
                node.Height = Math.Max(64, EstimateMermaidNodeHeight(node.Label, node.Width - 28, 32));
                break;
            default:
                node.Width = Math.Max(140, baseWidth);
                node.Height = Math.Max(64, EstimateMermaidNodeHeight(node.Label, node.Width - 28, 32));
                break;
        }
    }

    private static double EstimateMermaidNodeHeight(string label, double textWidth, double baseHeight)
    {
        var estimatedLineCount = Math.Max(1, (int)Math.Ceiling((Math.Max(1, label.Length) * 7.1) / Math.Max(96d, textWidth)));
        return baseHeight + ((estimatedLineCount - 1) * 18d);
    }

    private static FrameworkElement CreateMermaidNodeElement(MermaidNode node)
    {
        return node.Shape switch
        {
            MermaidNodeShape.Diamond => CreateDiamondNode(node),
            MermaidNodeShape.Rounded => CreateBoxNode(node, 10),
            _ => CreateBoxNode(node, 0)
        };
    }

    private static FrameworkElement CreateBoxNode(MermaidNode node, double cornerRadius)
    {
        var style = GetEffectiveNodeStyle(node);
        return new Border
        {
            Width = node.Width,
            Height = node.Height,
            Padding = new Thickness(12, 10, 12, 10),
            Background = style.Fill,
            BorderBrush = style.Stroke,
            BorderThickness = new Thickness(style.StrokeThickness),
            CornerRadius = new CornerRadius(cornerRadius),
            Child = new TextBlock
            {
                Text = node.Label,
                Foreground = style.Text,
                FontSize = 12.5,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
    }

    private static FrameworkElement CreateDiamondNode(MermaidNode node)
    {
        var style = GetEffectiveNodeStyle(node);
        var grid = new Grid
        {
            Width = node.Width,
            Height = node.Height
        };

        grid.Children.Add(new Polygon
        {
            Points = new PointCollection
            {
                new(node.Width / 2d, 0),
                new(node.Width, node.Height / 2d),
                new(node.Width / 2d, node.Height),
                new(0, node.Height / 2d)
            },
            Fill = style.Fill,
            Stroke = style.Stroke,
            StrokeThickness = style.StrokeThickness
        });

        grid.Children.Add(new TextBlock
        {
            Text = node.Label,
            Foreground = style.Text,
            FontSize = 12.5,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Width = node.Width * 0.64,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        return grid;
    }

    private static FrameworkElement CreateMermaidSubgraphElement(MermaidSubgraph subgraph)
    {
        return new Border
        {
            Width = subgraph.Width,
            Height = subgraph.Height,
            Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 12),
            Child = new DockPanel
            {
                LastChildFill = true,
                Children =
                {
                    new TextBlock
                    {
                        Text = subgraph.Label,
                        Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 6)
                    }
                }
            }
        };
    }

    private static MermaidRenderStyle GetEffectiveNodeStyle(MermaidNode node)
    {
        var style = MergeMermaidStyle(node.ClassStyle, node.InlineStyle);
        return new MermaidRenderStyle(
            ToBrush(style.Fill, Color.FromRgb(33, 37, 44)),
            ToBrush(style.Stroke, Color.FromArgb(205, 255, 255, 255)),
            ToBrush(style.Text, Colors.White),
            style.StrokeThickness ?? 1.1,
            ToDashArray(style.StrokeDashArray));
    }

    private static MermaidRenderStyle GetEffectiveEdgeStyle(MermaidEdge edge)
    {
        var dashed = edge.Arrow.IndexOf('.') >= 0;
        var thick = edge.Arrow.IndexOf('=') >= 0;
        var style = edge.InlineStyle;
        return new MermaidRenderStyle(
            Brushes.Transparent,
            ToBrush(style?.Stroke, Color.FromArgb(220, 255, 255, 255)),
            Brushes.White,
            style?.StrokeThickness ?? (thick ? 1.8 : 1.35),
            ToDashArray(style?.StrokeDashArray) ?? (dashed ? new DoubleCollection { 4, 4 } : null));
    }

    private static Brush ToBrush(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new SolidColorBrush(fallback);
        }

        try
        {
            var converter = new BrushConverter();
            if (converter.ConvertFromString(value) is Brush brush)
            {
                return brush;
            }
        }
        catch
        {
        }

        return new SolidColorBrush(fallback);
    }

    private static DoubleCollection? ToDashArray(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var values = new DoubleCollection();
        foreach (var part in parts)
        {
            if (TryParseCssDouble(part, out var parsed))
            {
                values.Add(parsed);
            }
        }

        return values.Count == 0 ? null : values;
    }

    private static void AddMermaidEdgeVisual(Canvas canvas, MermaidEdge edge)
    {
        var style = GetEffectiveEdgeStyle(edge);
        var fromPoint = GetMermaidEdgeStart(edge);
        var toPoint = GetMermaidEdgeEnd(edge);
        var route = BuildMermaidEdgeRoute(edge, fromPoint, toPoint);
        var geometry = CreateMermaidEdgeGeometry(route, out var labelAnchor, out var arrowBasePoint);
        var path = new Path
        {
            Data = geometry,
            Stroke = style.Stroke,
            StrokeThickness = style.StrokeThickness,
            SnapsToDevicePixels = true
        };
        if (style.StrokeDashArray is not null)
        {
            path.StrokeDashArray = style.StrokeDashArray;
        }
        Panel.SetZIndex(path, 1);
        canvas.Children.Add(path);

        var arrowHead = CreateArrowHead(arrowBasePoint, toPoint, style.Stroke);
        Panel.SetZIndex(arrowHead, 2);
        canvas.Children.Add(arrowHead);

        if (!string.IsNullOrWhiteSpace(edge.Label))
        {
            var label = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(90, 123, 193, 255)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 2, 5, 2),
                Child = new TextBlock
                {
                    Text = edge.Label,
                    Foreground = Brushes.White,
                    FontSize = 10
                }
            };

            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var labelYOffset = GetMermaidLabelYOffset(edge, fromPoint, toPoint);
            var labelPoint = new Point(
                labelAnchor.X - (label.DesiredSize.Width / 2d),
                labelAnchor.Y - label.DesiredSize.Height + labelYOffset);
            labelPoint = AdjustMermaidLabelPoint(edge, fromPoint, toPoint, label, labelPoint);

            Canvas.SetLeft(label, labelPoint.X);
            Canvas.SetTop(label, labelPoint.Y);
            Panel.SetZIndex(label, 12);
            canvas.Children.Add(label);
        }
    }

    private static Geometry CreateMermaidEdgeGeometry(IReadOnlyList<Point> route, out Point labelAnchor, out Point arrowBasePoint)
    {
        var figure = new PathFigure
        {
            StartPoint = route[0],
            IsClosed = false,
            IsFilled = false
        };

        const double cornerRadius = 10;
        for (var index = 1; index < route.Count; index++)
        {
            var previous = route[index - 1];
            var current = route[index];
            var next = index + 1 < route.Count ? route[index + 1] : (Point?)null;

            if (next is null || !IsOrthogonalTurn(previous, current, next.Value))
            {
                figure.Segments.Add(new LineSegment(current, true));
                continue;
            }

            var incoming = current - previous;
            incoming.Normalize();
            var outgoing = next.Value - current;
            outgoing.Normalize();

            var incomingLength = Distance(previous, current);
            var outgoingLength = Distance(current, next.Value);
            var radius = Math.Min(cornerRadius, Math.Min(incomingLength, outgoingLength) / 2d);

            var lineEnd = current - (incoming * radius);
            var lineStart = current + (outgoing * radius);
            figure.Segments.Add(new LineSegment(lineEnd, true));
            figure.Segments.Add(new QuadraticBezierSegment(current, lineStart, true));
        }

        labelAnchor = GetMermaidLabelAnchor(route);
        arrowBasePoint = route.Count >= 2 ? route[route.Count - 2] : route[0];

        return new PathGeometry(new[] { figure });
    }

    private static Point GetMermaidLabelAnchor(IReadOnlyList<Point> route)
    {
        var totalLength = 0d;
        for (var index = 1; index < route.Count; index++)
        {
            totalLength += Distance(route[index - 1], route[index]);
        }

        if (totalLength <= 0.001)
        {
            return route[0];
        }

        var halfway = totalLength / 2d;
        var traversed = 0d;
        for (var index = 1; index < route.Count; index++)
        {
            var start = route[index - 1];
            var end = route[index];
            var segmentLength = Distance(start, end);
            if (traversed + segmentLength >= halfway)
            {
                var t = (halfway - traversed) / Math.Max(0.001, segmentLength);
                return GetLinePoint(start, end, t);
            }

            traversed += segmentLength;
        }

        return route[route.Count - 1];
    }

    private static double GetMermaidLabelYOffset(MermaidEdge edge, Point fromPoint, Point toPoint)
    {
        if (Math.Abs(fromPoint.X - toPoint.X) < 8)
        {
            return edge.From.Shape == MermaidNodeShape.Diamond ? -10d : -8d;
        }

        return edge.From.Shape == MermaidNodeShape.Diamond ? -14d : -16d;
    }

    private static Point AdjustMermaidLabelPoint(MermaidEdge edge, Point fromPoint, Point toPoint, FrameworkElement label, Point labelPoint)
    {
        if (edge.From.Shape == MermaidNodeShape.Diamond && Math.Abs(fromPoint.X - toPoint.X) < 8)
        {
            return new Point(
                labelPoint.X + 24,
                labelPoint.Y - 4);
        }

        if (edge.From.Shape == MermaidNodeShape.Diamond)
        {
            var horizontalNudge = toPoint.X < fromPoint.X ? -6d : 6d;
            return new Point(
                labelPoint.X + horizontalNudge,
                labelPoint.Y - 2d);
        }

        return labelPoint;
    }

    private static Point GetLinePoint(Point start, Point end, double t)
    {
        return new Point(
            start.X + ((end.X - start.X) * t),
            start.Y + ((end.Y - start.Y) * t));
    }

    private static Point GetMermaidEdgeStart(MermaidEdge edge)
    {
        var sourceSide = ResolveMermaidExitSide(edge.From, edge.To);
        return GetMermaidNodeAnchor(edge.From, sourceSide);
    }

    private static Point GetMermaidEdgeEnd(MermaidEdge edge)
    {
        var targetSide = ResolveMermaidEntrySide(edge.From, edge.To);
        return GetMermaidNodeAnchor(edge.To, targetSide);
    }

    private static Polygon CreateArrowHead(Point fromPoint, Point toPoint, Brush fill)
    {
        var vector = fromPoint - toPoint;
        if (vector.Length < 0.001)
        {
            return new Polygon
            {
                Points = new PointCollection()
            };
        }

        vector.Normalize();

        var perpendicular = new Vector(-vector.Y, vector.X);
        const double arrowLength = 10;
        const double arrowWidth = 4;

        var basePoint = toPoint + (vector * arrowLength);

        return new Polygon
        {
            Fill = fill,
            Points = new PointCollection
            {
                toPoint,
                basePoint + (perpendicular * arrowWidth),
                basePoint - (perpendicular * arrowWidth)
            }
        };
    }

    private static List<Point> BuildMermaidEdgeRoute(MermaidEdge edge, Point fromPoint, Point toPoint)
    {
        var sourceSide = ResolveMermaidExitSide(edge.From, edge.To);
        var targetSide = ResolveMermaidEntrySide(edge.From, edge.To);
        const double offset = 18;

        var exitPoint = OffsetMermaidPoint(fromPoint, sourceSide, offset);
        var entryPoint = OffsetMermaidPoint(toPoint, targetSide, offset);
        var route = new List<Point> { fromPoint, exitPoint };

        var verticalFlow = sourceSide is MermaidNodeSide.Bottom or MermaidNodeSide.Top;
        if (verticalFlow)
        {
            var midY = (exitPoint.Y + entryPoint.Y) / 2d;
            route.Add(new Point(exitPoint.X, midY));
            route.Add(new Point(entryPoint.X, midY));
        }
        else
        {
            var midX = (exitPoint.X + entryPoint.X) / 2d;
            route.Add(new Point(midX, exitPoint.Y));
            route.Add(new Point(midX, entryPoint.Y));
        }

        route.Add(entryPoint);
        route.Add(toPoint);
        return SimplifyMermaidRoute(route);
    }

    private static List<Point> SimplifyMermaidRoute(IReadOnlyList<Point> points)
    {
        var simplified = new List<Point>();
        foreach (var point in points)
        {
            if (simplified.Count == 0 || Distance(simplified[simplified.Count - 1], point) > 0.5)
            {
                simplified.Add(point);
            }
        }

        for (var index = simplified.Count - 2; index >= 1; index--)
        {
            var previous = simplified[index - 1];
            var current = simplified[index];
            var next = simplified[index + 1];
            if ((Math.Abs(previous.X - current.X) < 0.5 && Math.Abs(current.X - next.X) < 0.5) ||
                (Math.Abs(previous.Y - current.Y) < 0.5 && Math.Abs(current.Y - next.Y) < 0.5))
            {
                simplified.RemoveAt(index);
            }
        }

        return simplified;
    }

    private static MermaidNodeSide ResolveMermaidExitSide(MermaidNode from, MermaidNode to)
    {
        var delta = GetMermaidNodeCenter(to) - GetMermaidNodeCenter(from);
        if (Math.Abs(delta.Y) >= Math.Abs(delta.X) * 0.75)
        {
            return delta.Y >= 0 ? MermaidNodeSide.Bottom : MermaidNodeSide.Top;
        }

        return delta.X >= 0 ? MermaidNodeSide.Right : MermaidNodeSide.Left;
    }

    private static MermaidNodeSide ResolveMermaidEntrySide(MermaidNode from, MermaidNode to)
    {
        var delta = GetMermaidNodeCenter(to) - GetMermaidNodeCenter(from);
        if (Math.Abs(delta.Y) >= Math.Abs(delta.X) * 0.75)
        {
            return delta.Y >= 0 ? MermaidNodeSide.Top : MermaidNodeSide.Bottom;
        }

        return delta.X >= 0 ? MermaidNodeSide.Left : MermaidNodeSide.Right;
    }

    private static Point GetMermaidNodeAnchor(MermaidNode node, MermaidNodeSide side)
    {
        var center = GetMermaidNodeCenter(node);
        if (node.Shape == MermaidNodeShape.Diamond)
        {
            return side switch
            {
                MermaidNodeSide.Top => new Point(center.X, node.Y),
                MermaidNodeSide.Bottom => new Point(center.X, node.Y + node.Height),
                MermaidNodeSide.Left => new Point(node.X, center.Y),
                _ => new Point(node.X + node.Width, center.Y)
            };
        }

        return side switch
        {
            MermaidNodeSide.Top => new Point(center.X, node.Y),
            MermaidNodeSide.Bottom => new Point(center.X, node.Y + node.Height),
            MermaidNodeSide.Left => new Point(node.X, center.Y),
            _ => new Point(node.X + node.Width, center.Y)
        };
    }

    private static Point GetMermaidNodeCenter(MermaidNode node)
    {
        return new Point(node.X + (node.Width / 2d), node.Y + (node.Height / 2d));
    }

    private static Point OffsetMermaidPoint(Point point, MermaidNodeSide side, double distance)
    {
        return side switch
        {
            MermaidNodeSide.Top => new Point(point.X, point.Y - distance),
            MermaidNodeSide.Bottom => new Point(point.X, point.Y + distance),
            MermaidNodeSide.Left => new Point(point.X - distance, point.Y),
            _ => new Point(point.X + distance, point.Y)
        };
    }

    private static bool IsOrthogonalTurn(Point previous, Point current, Point next)
    {
        var previousVertical = Math.Abs(previous.X - current.X) < 0.5;
        var nextVertical = Math.Abs(current.X - next.X) < 0.5;
        var previousHorizontal = Math.Abs(previous.Y - current.Y) < 0.5;
        var nextHorizontal = Math.Abs(current.Y - next.Y) < 0.5;
        return (previousVertical && nextHorizontal) || (previousHorizontal && nextVertical);
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static Brush CreateMermaidCanvasBackground()
    {
        var brush = new SolidColorBrush(Color.FromRgb(58, 58, 58));
        brush.Freeze();
        return brush;
    }

    private static string NormalizeMermaidEdgeLine(string rawLine)
    {
        var line = (rawLine ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        if (line.StartsWith("graph ", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("flowchart ", StringComparison.OrdinalIgnoreCase))
        {
            var separatorIndex = line.IndexOf(' ');
            if (separatorIndex >= 0)
            {
                line = line.Substring(separatorIndex + 1).TrimStart();
            }
        }

        foreach (var direction in new[] { "TD ", "TB ", "BT ", "LR ", "RL " })
        {
            if (line.StartsWith(direction, StringComparison.OrdinalIgnoreCase))
            {
                line = line.Substring(direction.Length).TrimStart();
                break;
            }
        }

        return line;
    }

    private static string GetLanguageDisplayName(string? language)
    {
        var normalized = (language ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "Código";
        }

        return normalized.ToLowerInvariant() switch
        {
            "csharp" or "cs" or "c#" => "C#",
            "fsharp" or "fs" or "f#" => "F#",
            "vb" or "vbnet" => "VB.NET",
            "javascript" or "js" => "JavaScript",
            "typescript" or "ts" => "TypeScript",
            "json" => "JSON",
            "html" => "HTML",
            "xml" => "XML",
            "xaml" => "XAML",
            "css" => "CSS",
            "scss" => "SCSS",
            "sql" => "SQL",
            "yaml" or "yml" => "YAML",
            "md" or "markdown" => "Markdown",
            "powershell" or "ps1" or "pwsh" => "PowerShell",
            "bash" or "sh" or "shell" => "Shell",
            "plaintext" or "text" => "Texto",
            "mermaid" => "Mermaid",
            "dockerfile" => "Dockerfile",
            _ => normalized.Length <= 4
                ? normalized.ToUpperInvariant()
                : char.ToUpperInvariant(normalized[0]) + normalized.Substring(1)
        };
    }

    private static FenceHeader ParseFenceHeader(string line)
    {
        var info = line.TrimStart();
        if (!info.StartsWith("```", StringComparison.Ordinal))
        {
            return new FenceHeader(string.Empty, string.Empty);
        }

        info = info.Substring(3).Trim();
        if (string.IsNullOrWhiteSpace(info))
        {
            return new FenceHeader(string.Empty, string.Empty);
        }

        if (info.StartsWith("mermaid", StringComparison.OrdinalIgnoreCase))
        {
            var remainder = info.Substring("mermaid".Length).TrimStart();
            return new FenceHeader("mermaid", remainder);
        }

        var separatorIndex = info.IndexOfAny(new[] { ' ', '\t' });
        if (separatorIndex < 0)
        {
            return new FenceHeader(info.Trim(), string.Empty);
        }

        var language = info.Substring(0, separatorIndex).Trim();
        var inlineContent = info.Substring(separatorIndex + 1).TrimStart();
        return new FenceHeader(language, inlineContent);
    }

    private sealed class InlineMatch
    {
        private readonly Func<Inline> _inlineFactory;

        public InlineMatch(int index, int length, Func<Inline> inlineFactory)
        {
            Index = index;
            Length = length;
            _inlineFactory = inlineFactory;
        }

        public int Index { get; }

        public int Length { get; }

        public Inline ToInline() => _inlineFactory();
    }

    private sealed class MermaidDiagram
    {
        public Dictionary<string, MermaidNode> Nodes { get; } = new(StringComparer.Ordinal);

        public List<MermaidEdge> Edges { get; } = new();

        public Dictionary<string, MermaidStyle> ClassDefinitions { get; } = new(StringComparer.Ordinal);

        public List<MermaidSubgraph> Subgraphs { get; } = new();

        public MermaidStyle? DefaultLinkStyle { get; set; }

        public double CanvasWidth { get; set; }

        public double CanvasHeight { get; set; }
    }

    private sealed class MermaidNode
    {
        public MermaidNode(string id, string label, MermaidNodeShape shape)
        {
            Id = id;
            Label = label;
            Shape = shape;
        }

        public string Id { get; }

        public string Label { get; set; }

        public MermaidNodeShape Shape { get; set; }

        public List<string> ClassNames { get; } = new();

        public MermaidStyle? ClassStyle { get; set; }

        public MermaidStyle? InlineStyle { get; set; }

        public int Level { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }
    }

    private sealed class MermaidEdge
    {
        public MermaidEdge(MermaidNode from, MermaidNode to, string arrow, string label)
        {
            From = from;
            To = to;
            Arrow = arrow;
            Label = label;
        }

        public MermaidNode From { get; }

        public MermaidNode To { get; }

        public string Arrow { get; }

        public string Label { get; }

        public MermaidStyle? InlineStyle { get; set; }
    }

    private sealed class MermaidSubgraph
    {
        public MermaidSubgraph(string id, string label)
        {
            Id = id;
            Label = label;
        }

        public string Id { get; }

        public string Label { get; }

        public string? ParentId { get; set; }

        public int Depth { get; set; }

        public HashSet<string> NodeIds { get; } = new(StringComparer.Ordinal);

        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }
    }

    private sealed class MermaidStyle
    {
        public string? Fill { get; set; }

        public string? Stroke { get; set; }

        public string? Text { get; set; }

        public double? StrokeThickness { get; set; }

        public string? StrokeDashArray { get; set; }

        public MermaidStyle Clone()
        {
            return new MermaidStyle
            {
                Fill = Fill,
                Stroke = Stroke,
                Text = Text,
                StrokeThickness = StrokeThickness,
                StrokeDashArray = StrokeDashArray
            };
        }
    }

    private sealed class MermaidRenderStyle
    {
        public MermaidRenderStyle(Brush fill, Brush stroke, Brush text, double strokeThickness, DoubleCollection? strokeDashArray)
        {
            Fill = fill;
            Stroke = stroke;
            Text = text;
            StrokeThickness = strokeThickness;
            StrokeDashArray = strokeDashArray;
        }

        public Brush Fill { get; }

        public Brush Stroke { get; }

        public Brush Text { get; }

        public double StrokeThickness { get; }

        public DoubleCollection? StrokeDashArray { get; }
    }

    private enum MermaidNodeShape
    {
        Rectangle,
        Rounded,
        Diamond
    }

    private enum MermaidNodeSide
    {
        Top,
        Right,
        Bottom,
        Left
    }

    private sealed class FenceHeader
    {
        public FenceHeader(string language, string inlineContent)
        {
            Language = language;
            InlineContent = inlineContent;
        }

        public string Language { get; }

        public string InlineContent { get; }
    }
}
