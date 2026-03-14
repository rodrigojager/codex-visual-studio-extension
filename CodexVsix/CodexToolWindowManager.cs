using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace CodexVsix;

internal static class CodexToolWindowManager
{
    private static AsyncPackage? _package;
    private static ToolWindowPane? _settingsWindow;

    public static void Initialize(AsyncPackage package)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _package = package;
    }

    public static void ShowSettingsToolWindow(string section)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(() => ShowSettingsToolWindowAsync(section));
    }

    private static async Task ShowSettingsToolWindowAsync(string section)
    {
        var package = _package;
        if (package is null)
        {
            return;
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        try
        {
            CodexViewModelHost.GetOrCreate().EnsureExternalSettingsSection(section);
            var window = await package.ShowToolWindowAsync(typeof(CodexSettingsToolWindow), 0, true, package.DisposalToken);
            if (window?.Frame is null)
            {
                throw new NotSupportedException("Nao foi possivel criar a janela de configuracoes do Codex.");
            }

            _settingsWindow = window;
            UpdateWindowCaption(window, CodexViewModelHost.GetOrCreate().Localization);
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError("CodexVsix", "Falha ao abrir a janela de configuracoes do Codex." + Environment.NewLine + ex);
        }
    }

    public static void RefreshSettingsToolWindowCaption(Services.LocalizationService localization)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_settingsWindow is not null)
            {
                UpdateWindowCaption(_settingsWindow, localization);
            }
        });
    }

    private static void UpdateWindowCaption(ToolWindowPane window, Services.LocalizationService localization)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        window.Caption = localization.CodexSettingsNav;
    }
}
