using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodexVsix.Models;
using CodexVsix.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CodexVsix.Tests;

public sealed class CodexCliInstallerTests
{
    [Fact]
    public void SearchPathRefreshDeduplicatesInheritedAndPersistedEntries()
        => Assert.Equal(@"D:\node;C:\Windows\System32;D:\npm", CodexCliInstaller.MergeSearchPaths(
            @"D:\node;C:\Windows\System32", @"d:\NODE;D:\npm", @"C:\Windows\System32"));

    [Fact]
    public async Task RefreshedPathStillResolvesSystemExecutables()
    {
        var result = await CodexCliInstaller.RunAsync("where.exe cmd.exe", new CodexExtensionSettings(), TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.True(result.Success, result.Output);
        Assert.Contains("cmd.exe", result.Output);
    }

    private static string SlowCommand => "\"" + Path.Combine(Environment.SystemDirectory, "ping.exe") + "\" -n 30 127.0.0.1";
    [Theory]
    [InlineData("codex", true)]
    [InlineData("codex.cmd", true)]
    [InlineData("codex.exe", true)]
    [InlineData("", true)]
    [InlineData("D:\\missing\\codex.exe", false)]
    [InlineData("company-codex.cmd", false)]
    public void DoesNotReplaceExplicitCustomExecutables(string command, bool allowed)
        => Assert.Equal(allowed, CodexCliInstaller.CanInstallForConfiguredCommand(command));

    [Fact]
    public async Task MissingCliAccountReadShowsSignInWithoutLaunchingInstaller()
    {
        using var service = new CodexProcessService();
        service.MissingExecutableHandler = (_, _, _) => throw new Exception("No startup popup expected");
        var settings = new CodexExtensionSettings { CodexExecutablePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe") };
        var result = await service.InvokeAppServerRequestAsync(settings, "account/read", new JObject(), CancellationToken.None);
        Assert.True(result!["requiresOpenaiAuth"]!.Value<bool>());
        Assert.Equal(JTokenType.Null, result["account"]!.Type);
    }

    [Fact]
    public async Task SignInRequestsInstallAndDeclineDoesNotStartServer()
    {
        using var service = new CodexProcessService();
        var calls = 0;
        service.MissingExecutableHandler = (_, userInitiated, _) => { Assert.True(userInitiated); calls++; return Task.FromResult(false); };
        var settings = new CodexExtensionSettings { CodexExecutablePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InvokeAppServerRequestAsync(settings, "account/login/start", new JObject(), CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task BackgroundCheckNeverPromptsForMissingCli()
    {
        var settings = new CodexExtensionSettings { CodexExecutablePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe") };
        Assert.False(await CodexCliInstaller.EnsureInstalledAsync(settings, false, CancellationToken.None));
    }

    [Fact]
    public async Task ProcessRunnerPreservesOutputAndFailure()
    {
        var ok = await CodexCliInstaller.RunAsync("echo installer-fixture", new CodexExtensionSettings(), TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.True(ok.Success);
        Assert.Contains("installer-fixture", ok.Output);
        var failed = await CodexCliInstaller.RunAsync("exit /b 7", new CodexExtensionSettings(), TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.False(failed.Success);
    }

    [Fact]
    public async Task TimeoutTerminatesTheStartedProcessTreeAndNextAttemptWorks()
    {
        var timer = Stopwatch.StartNew();
        var timedOut = await CodexCliInstaller.RunAsync(SlowCommand, new CodexExtensionSettings(), TimeSpan.FromMilliseconds(300), CancellationToken.None);
        Assert.False(timedOut.Success);
        Assert.Equal("Timed out.", timedOut.Output);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15));
        var retry = await CodexCliInstaller.RunAsync("echo retry-ok", new CodexExtensionSettings(), TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.True(retry.Success);
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        using var cancel = new CancellationTokenSource(300);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CodexCliInstaller.RunAsync(
            SlowCommand, new CodexExtensionSettings(), TimeSpan.FromSeconds(30), cancel.Token));
    }
}
