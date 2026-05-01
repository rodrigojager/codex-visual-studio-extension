using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using CodexVsix.Services;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CodexVsix;

internal sealed class ShowCodexToolWindowCommand
{
    private readonly AsyncPackage _package;

    private ShowCodexToolWindowCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        var commandId = new CommandID(new Guid(GuidList.CommandSetString), PackageIds.ShowToolWindowCommand);
        var menuCommand = new MenuCommand((_, _) => _package.JoinableTaskFactory.RunAsync(ExecuteAsync).FileAndForget("CodexVsix/ShowToolWindow"), commandId);
        commandService.AddCommand(menuCommand);
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (commandService is not null)
        {
            _ = new ShowCodexToolWindowCommand(package, commandService);
        }
    }

    private async Task ExecuteAsync()
    {
        try
        {
            ToolWindowPane? existingWindow = await _package.FindToolWindowAsync(typeof(CodexToolWindow), 0, false, _package.DisposalToken);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
            if (existingWindow?.Frame is IVsWindowFrame existingFrame && IsFrameVisible(existingFrame))
            {
                existingFrame.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_NoSave);
                return;
            }

            ToolWindowPane window = await _package.FindToolWindowAsync(typeof(CodexToolWindow), 0, true, _package.DisposalToken);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
            if (window?.Frame is not IVsWindowFrame frame)
            {
                throw new NotSupportedException(new LocalizationService().OpenWindowFailedMessage);
            }

            frame.Show();
        }
        catch (Exception ex)
        {
            var localization = new LocalizationService();
            ActivityLog.TryLogError("CodexVsix", localization.ToolWindowOpenLogMessage + Environment.NewLine + ex);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            VsShellUtilities.ShowMessageBox(
                _package,
                localization.OpenWindowFailedMessage + Environment.NewLine + Environment.NewLine + ex.Message,
                "Codex",
                OLEMSGICON.OLEMSGICON_CRITICAL,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }

    private static bool IsFrameVisible(IVsWindowFrame frame)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return frame.IsVisible() == 0;
    }
}
