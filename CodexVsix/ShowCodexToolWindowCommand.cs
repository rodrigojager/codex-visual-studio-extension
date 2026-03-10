using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

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
        ToolWindowPane window = await _package.ShowToolWindowAsync(typeof(CodexToolWindow), 0, true, _package.DisposalToken);
        if (window?.Frame is null)
        {
            throw new NotSupportedException("Unable to create Codex tool window.");
        }
    }
}
