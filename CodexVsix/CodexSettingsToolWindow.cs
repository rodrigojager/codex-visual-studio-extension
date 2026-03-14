using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;

namespace CodexVsix;

public sealed class CodexSettingsToolWindow : ToolWindowPane
{
    public CodexSettingsToolWindow() : base(null)
    {
        Caption = "Codex Settings";

        try
        {
            Content = new CodexSettingsToolWindowControl();
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError("CodexVsix", "Falha ao inicializar a janela de configuracoes do Codex." + Environment.NewLine + ex);
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
                    Text = "A janela de configuracoes do Codex encontrou um erro durante a inicializacao."
                        + Environment.NewLine
                        + Environment.NewLine
                        + ex.Message,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
    }
}
