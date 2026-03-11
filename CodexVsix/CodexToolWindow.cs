using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;

namespace CodexVsix;

public sealed class CodexToolWindow : ToolWindowPane
{
    public CodexToolWindow() : base(null)
    {
        Caption = "Codex";

        try
        {
            Content = new CodexToolWindowControl();
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError("CodexVsix", "Falha ao inicializar o conteúdo da janela do Codex." + Environment.NewLine + ex);
            Content = CreateErrorView(ex);
        }
    }

    private static FrameworkElement CreateErrorView(Exception ex)
    {
        return new Border
        {
            Padding = new Thickness(16),
            Background = Brushes.Transparent,
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = "A janela do Codex encontrou um erro durante a inicialização."
                        + Environment.NewLine
                        + Environment.NewLine
                        + ex.Message,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
    }
}
