using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodexVsix.Models;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json.Linq;

namespace CodexVsix.Services;

internal static class CodexCliInstaller
{
	private const int IdYes = 6;
	private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(5);
	private static readonly TimeSpan ProcessTerminationTimeout = TimeSpan.FromSeconds(5);
	private static readonly SemaphoreSlim Gate = new(1, 1);

	// Returns true when a Codex executable can be resolved afterwards (already present or just installed).
	public static Task<bool> EnsureInstalledAsync(CodexExtensionSettings settings, bool userInitiated, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		// Keep background probes independent of VS UI services and never prompt at startup.
		return userInitiated ? EnsureUserRequestedInstallAsync(settings, cancellationToken) : Task.FromResult(IsResolvable(settings));
	}

	private static async Task<bool> EnsureUserRequestedInstallAsync(CodexExtensionSettings settings, CancellationToken cancellationToken)
	{
		await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (IsResolvable(settings))
			{
				return true;
			}

			if (!CanInstallForConfiguredCommand(settings.CodexExecutablePath))
			{
				await ShowMessageAsync("The configured Codex executable was not found. Open the extension settings and correct its path, or select the default codex command.",
					OLEMSGBUTTON.OLEMSGBUTTON_OK, cancellationToken).ConfigureAwait(false);
				return false;
			}

			var hasNpm = await RunAsync("npm --version", settings, TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
			var accepted = await AskUserAsync(hasNpm.Success, cancellationToken).ConfigureAwait(false);
			if (!accepted)
			{
				Log("cli.install.declined", hasNpm.Success ? "user-declined" : "npm-missing");
				return false;
			}

			Log("cli.install.started", CodexEnvironmentService.FallbackInstallCommand);
			await SetStatusAsync("Installing Codex CLI (usually under a minute)...", true, cancellationToken).ConfigureAwait(false);
			(bool Success, string Output) install;
			try
			{
				install = await RunAsync(CodexEnvironmentService.FallbackInstallCommand, settings, InstallTimeout, cancellationToken).ConfigureAwait(false);
			}
			finally
			{
				await SetStatusAsync(string.Empty, false, CancellationToken.None).ConfigureAwait(false);
			}

			Log(install.Success ? "cli.install.succeeded" : "cli.install.failed", "npm completed");

			if (install.Success && IsResolvable(settings))
			{
				return true;
			}

			await ShowMessageAsync(
				"Codex CLI installation did not complete." + Environment.NewLine + Environment.NewLine
				+ "Run this command in a terminal, then try again:" + Environment.NewLine
				+ CodexEnvironmentService.FallbackInstallCommand,
				OLEMSGBUTTON.OLEMSGBUTTON_OK,
				cancellationToken).ConfigureAwait(false);
			return false;
		}
		finally
		{
			Gate.Release();
		}
	}

	internal static bool CanInstallForConfiguredCommand(string? configuredPath)
	{
		var command = (configuredPath ?? string.Empty).Trim().Trim('"');
		return command.Length == 0 || command.Equals("codex", StringComparison.OrdinalIgnoreCase)
			|| command.Equals("codex.cmd", StringComparison.OrdinalIgnoreCase)
			|| command.Equals("codex.exe", StringComparison.OrdinalIgnoreCase)
			|| command.Equals("codex.ps1", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsResolvable(CodexExtensionSettings settings)
	{
		return !string.IsNullOrWhiteSpace(
			CodexExecutableResolver.ResolveExecutableLocation(settings.CodexExecutablePath, settings.EnvironmentVariables));
	}

	private static async Task<bool> AskUserAsync(bool hasNpm, CancellationToken cancellationToken)
	{
		if (!hasNpm)
		{
			await ShowMessageAsync(
				"The Codex CLI is required to sign in and use Codex, but it was not found, and npm (Node.js) is not available to install it." + Environment.NewLine + Environment.NewLine
				+ "Install Node.js from https://nodejs.org, then run:" + Environment.NewLine
				+ CodexEnvironmentService.FallbackInstallCommand,
				OLEMSGBUTTON.OLEMSGBUTTON_OK,
				cancellationToken).ConfigureAwait(false);
			return false;
		}

		var result = await ShowMessageAsync(
			"The Codex CLI is required to sign in and use Codex, but it was not found on your system." + Environment.NewLine + Environment.NewLine
			+ "Would you like the Codex extension to install it automatically?" + Environment.NewLine
			+ "(Command: " + CodexEnvironmentService.FallbackInstallCommand + ")" + Environment.NewLine + Environment.NewLine
			+ "Installation usually takes under a minute. An \"Installing Codex CLI\" message will show in the status bar at the bottom while it runs.",
			OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
			cancellationToken).ConfigureAwait(false);
		return result == IdYes;
	}

	private static async Task SetStatusAsync(string text, bool animate, CancellationToken cancellationToken)
	{
		await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		if (Package.GetGlobalService(typeof(SVsStatusbar)) is not IVsStatusbar statusBar)
		{
			return;
		}

		object icon = (short)Microsoft.VisualStudio.Shell.Interop.Constants.SBAI_General;
		statusBar.Animation(animate ? 1 : 0, ref icon);
		if (string.IsNullOrEmpty(text))
		{
			statusBar.Clear();
		}
		else
		{
			statusBar.SetText(text);
		}
	}

	private static async Task<int> ShowMessageAsync(string message, OLEMSGBUTTON buttons, CancellationToken cancellationToken)
	{
		await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		return VsShellUtilities.ShowMessageBox(
			Microsoft.VisualStudio.Shell.ServiceProvider.GlobalProvider,
			message,
			"Codex CLI Required",
			OLEMSGICON.OLEMSGICON_QUERY,
			buttons,
			OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
	}

	internal static async Task<(bool Success, string Output)> RunAsync(string commandLine, CodexExtensionSettings settings, TimeSpan timeout, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var commandShell = Environment.GetEnvironmentVariable("ComSpec");
		if (string.IsNullOrWhiteSpace(commandShell))
		{
			commandShell = "cmd.exe";
		}

		var psi = new ProcessStartInfo
		{
			FileName = commandShell,
			Arguments = "/d /s /c \"" + commandLine + "\"",
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};

		var overrides = CodexEnvironmentPathHelper.ParseEnvironmentVariables(settings.EnvironmentVariables);
		// Pick up Node.js installed after the IDE was launched, while respecting explicit overrides.
		if (!overrides.ContainsKey("PATH"))
		{
			psi.EnvironmentVariables["PATH"] = MergeSearchPaths(psi.EnvironmentVariables["PATH"],
				Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
				Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));
		}
		foreach (var entry in overrides)
		{
			psi.EnvironmentVariables[entry.Key] = entry.Value;
		}

		try
		{
			using var process = new Process { StartInfo = psi };
			var output = new StringBuilder();
			var standardOutputCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			var standardErrorCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			process.OutputDataReceived += (_, e) => AppendOutput(output, e.Data);
			process.OutputDataReceived += (_, e) => { if (e.Data is null) { standardOutputCompleted.TrySetResult(true); } };
			process.ErrorDataReceived += (_, e) => AppendOutput(output, e.Data);
			process.ErrorDataReceived += (_, e) => { if (e.Data is null) { standardErrorCompleted.TrySetResult(true); } };
			process.EnableRaisingEvents = true;
			process.Exited += (_, _) => exited.TrySetResult(true);
			process.Start();
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();

			using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeoutSource.CancelAfter(timeout);
			using var cancellationRegistration = timeoutSource.Token.Register(() => exited.TrySetCanceled());
			try
			{
				await exited.Task.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				TerminateProcessTree(process);
				await DrainOutputAsync(standardOutputCompleted.Task, standardErrorCompleted.Task).ConfigureAwait(false);
				cancellationToken.ThrowIfCancellationRequested();
				return (false, "Timed out.");
			}

			await DrainOutputAsync(standardOutputCompleted.Task, standardErrorCompleted.Task).ConfigureAwait(false);

			lock (output)
			{
				return (process.ExitCode == 0, output.ToString());
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			return (false, ex.Message);
		}
	}

	// cmd.exe drops PATH when duplicate inherited/persisted entries push it beyond its limit.
	internal static string MergeSearchPaths(params string?[] paths)
		=> string.Join(";", paths.SelectMany(path => (path ?? string.Empty).Split(';'))
			.Select(path => Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')))
			.Where(path => path.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));

	private static void AppendOutput(StringBuilder output, string? line)
	{
		if (line is null) return;
		lock (output)
		{
			const int limit = 65536;
			var remaining = limit - output.Length;
			if (remaining > 0) output.Append(line, 0, Math.Min(line.Length, remaining));
			if (output.Length < limit) output.AppendLine();
		}
	}

	private static async Task DrainOutputAsync(Task standardOutputCompleted, Task standardErrorCompleted)
	{
		var outputCompletion = Task.WhenAll(standardOutputCompleted, standardErrorCompleted);
		await Task.WhenAny(outputCompletion, Task.Delay(ProcessTerminationTimeout)).ConfigureAwait(false);
	}

	private static void TerminateProcessTree(Process process)
	{
		try
		{
			if (process.HasExited)
			{
				return;
			}
		}
		catch
		{
			return;
		}

		try
		{
			using var taskKill = Process.Start(new ProcessStartInfo
			{
				FileName = Path.Combine(Environment.SystemDirectory, "taskkill.exe"),
				Arguments = "/pid " + process.Id + " /t /f",
				UseShellExecute = false,
				CreateNoWindow = true
			});
			taskKill?.WaitForExit((int)ProcessTerminationTimeout.TotalMilliseconds);
		}
		catch
		{
		}

		try
		{
			if (!process.HasExited)
			{
				process.Kill();
				process.WaitForExit((int)ProcessTerminationTimeout.TotalMilliseconds);
			}
		}
		catch
		{
			// Best effort: diagnostics must never keep the installer gate held.
		}
	}

	private static void Log(string eventName, string detail)
	{
		CodexDiagnosticLogger.Shared.Write(eventName, new JObject { ["detail"] = detail });
	}

}
