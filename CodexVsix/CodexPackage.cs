using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace CodexVsix;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("Visual Codex Studio", "Tool window integration for Codex", "1.2.0")]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideToolWindow(typeof(CodexToolWindow))]
[ProvideToolWindow(typeof(CodexSettingsToolWindow))]
[Guid(GuidList.PackageString)]
public sealed class CodexPackage : AsyncPackage
{
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        CodexToolWindowManager.Initialize(this);
        await ShowCodexToolWindowCommand.InitializeAsync(this);
    }
}
