using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodexVsix.Models;
using Newtonsoft.Json.Linq;

namespace CodexVsix.Services;

public sealed class CodexEnvironmentService
{
    public const string FallbackInstallCommand = "npm install -g @openai/codex";

    public async Task<CodexEnvironmentStatus> InspectAsync(CodexExtensionSettings settings, CancellationToken cancellationToken)
    {
        var localization = new LocalizationService(settings.LanguageOverride);
        var configuredExecutablePath = CodexExecutableResolver.NormalizeConfiguredExecutablePath(settings.CodexExecutablePath);
        var status = new CodexEnvironmentStatus
        {
            Stage = CodexSetupStage.Checking,
            ConfiguredExecutablePath = configuredExecutablePath,
            AuthFilePath = GetAuthFilePath(settings.EnvironmentVariables)
        };

        try
        {
            var resolvedExecutablePath = CodexExecutableResolver.ResolveExecutableLocation(configuredExecutablePath, settings.EnvironmentVariables);
            if (string.IsNullOrWhiteSpace(resolvedExecutablePath))
            {
                status.Stage = CodexSetupStage.MissingExecutable;
                return status;
            }

            status.ResolvedExecutablePath = resolvedExecutablePath;

            var version = await TryGetVersionAsync(resolvedExecutablePath, localization, cancellationToken).ConfigureAwait(false);
            if (!version.Success)
            {
                status.Stage = CodexSetupStage.Error;
                status.ErrorDetail = version.Detail;
                return status;
            }

            status.Version = version.Detail;
            var appServer = await TryValidateAppServerAsync(resolvedExecutablePath, localization, cancellationToken).ConfigureAwait(false);
            if (!appServer.Success)
            {
                status.Stage = CodexSetupStage.Error;
                status.ErrorDetail = appServer.Detail;
                return status;
            }

            // The auth file may contain either OAuth tokens or a persisted OPENAI_API_KEY.
            // Treat both as authentication signals when computing setup readiness.
            var authFileInspection = InspectAuthFile(status.AuthFilePath);
            status.HasAuthFile = authFileInspection.HasUsableAuthFile;
            status.HasApiKey = HasApiKey(settings.EnvironmentVariables)
                || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
                || authFileInspection.HasEmbeddedApiKey;
            status.AccountEmail = authFileInspection.AccountEmail;

            status.Stage = status.HasApiKey || status.HasAuthFile
                ? CodexSetupStage.Ready
                : CodexSetupStage.MissingAuthentication;

            return status;
        }
        catch (Exception ex)
        {
            status.Stage = CodexSetupStage.Error;
            status.ErrorDetail = ex.Message;
            return status;
        }
    }

    public void LaunchLoginTerminal(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        if (IsPowerShellScript(executablePath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ResolvePowerShellHost(),
                Arguments = "-NoExit -ExecutionPolicy Bypass -File " + QuoteArgument(executablePath) + " login",
                UseShellExecute = true
            });
            return;
        }

        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            var commandShell = Environment.GetEnvironmentVariable("ComSpec");
            if (string.IsNullOrWhiteSpace(commandShell))
            {
                commandShell = "cmd.exe";
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = commandShell,
                Arguments = "/k \"" + QuoteForCommandShell(executablePath) + " login\"",
                UseShellExecute = true
            });
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = "login",
            UseShellExecute = true
        });
    }

    public void DeleteAuthFile(string? authFilePath = null)
    {
        var path = string.IsNullOrWhiteSpace(authFilePath)
            ? GetAuthFilePath()
            : authFilePath!;

        if (!File.Exists(path))
        {
            return;
        }

        File.Delete(path);
    }

    public string GetAuthFilePath(string? environmentVariables = null)
    {
        return Path.Combine(CodexEnvironmentPathHelper.GetCodexHomeDirectory(environmentVariables), "auth.json");
    }

    private static async Task<(bool Success, string Detail)> TryGetVersionAsync(string executablePath, LocalizationService localization, CancellationToken cancellationToken)
    {
        var probe = await RunProbeAsync(executablePath, "--version", localization.SetupErrorSummary, cancellationToken).ConfigureAwait(false);
        if (!probe.Success)
        {
            return probe;
        }

        var versionText = FirstNonEmptyLine(probe.Detail);
        return (true, string.IsNullOrWhiteSpace(versionText) ? localization.CodexDetectedLabel : versionText);
    }

    private static async Task<(bool Success, string Detail)> TryValidateAppServerAsync(string executablePath, LocalizationService localization, CancellationToken cancellationToken)
    {
        var probe = await RunProbeAsync(
            executablePath,
            "app-server --help",
            localization.AppServerValidationFailed,
            cancellationToken).ConfigureAwait(false);

        if (probe.Success)
        {
            return probe;
        }

        return (false, localization.AppServerUnsupported + Environment.NewLine + probe.Detail);
    }

    private static async Task<(bool Success, string Detail)> RunProbeAsync(string executablePath, string arguments, string fallbackError, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = CreateProbeStartInfo(executablePath, arguments)
        };

        try
        {
            if (!process.Start())
            {
                return (false, fallbackError);
            }
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var exited = await WaitForExitAsync(process, 5000, cancellationToken).ConfigureAwait(false);

        if (!exited)
        {
            TryTerminateProcess(process);
            return (false, fallbackError);
        }

        var output = await outputTask.ConfigureAwait(false);
        var errorText = await errorTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(errorText) ? output : errorText;
            return (false, string.IsNullOrWhiteSpace(error) ? fallbackError : error.Trim());
        }

        return (true, string.IsNullOrWhiteSpace(output) ? errorText : output);
    }

    private static ProcessStartInfo CreateProbeStartInfo(string executablePath, string arguments)
    {
        if (IsPowerShellScript(executablePath))
        {
            return new ProcessStartInfo
            {
                FileName = ResolvePowerShellHost(),
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + QuoteArgument(executablePath) + (string.IsNullOrWhiteSpace(arguments) ? string.Empty : " " + arguments),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        if (!RequiresCommandShell(executablePath))
        {
            return new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        var commandShell = Environment.GetEnvironmentVariable("ComSpec");
        if (string.IsNullOrWhiteSpace(commandShell))
        {
            commandShell = "cmd.exe";
        }

        return new ProcessStartInfo
        {
            FileName = commandShell,
            Arguments = "/d /s /c \"" + QuoteForCommandShell(executablePath) + (string.IsNullOrWhiteSpace(arguments) ? string.Empty : " " + arguments) + "\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    private static async Task<bool> WaitForExitAsync(Process process, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() => process.WaitForExit(timeoutMilliseconds), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryTerminateProcess(process);
            throw;
        }
    }

    private static string FirstNonEmptyLine(string? text)
    {
        return (text ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
            ?? string.Empty;
    }

    private static bool HasApiKey(string environmentVariables)
    {
        foreach (var line in (environmentVariables ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line.Substring(0, separatorIndex).Trim();
            var value = line.Substring(separatorIndex + 1).Trim();
            if (string.Equals(key, "OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
            {
                return true;
            }
        }

        return false;
    }

    private static AuthFileInspection InspectAuthFile(string authFilePath)
    {
        if (string.IsNullOrWhiteSpace(authFilePath) || !File.Exists(authFilePath))
        {
            return AuthFileInspection.Empty;
        }

        try
        {
            var document = JObject.Parse(File.ReadAllText(authFilePath));
            var embeddedApiKey = document["OPENAI_API_KEY"]?.Value<string>();
            var tokens = document["tokens"] as JObject;
            var accessToken = tokens?["access_token"]?.Value<string>();
            var refreshToken = tokens?["refresh_token"]?.Value<string>();
            var idToken = tokens?["id_token"]?.Value<string>();
            var hasEmbeddedApiKey = !string.IsNullOrWhiteSpace(embeddedApiKey);
            // "Usable" means we can authenticate requests (token pair or embedded API key),
            // not just that profile metadata (id_token) exists.
            var hasUsableAuthFile = hasEmbeddedApiKey
                || !string.IsNullOrWhiteSpace(accessToken)
                || !string.IsNullOrWhiteSpace(refreshToken);

            if (string.IsNullOrWhiteSpace(idToken))
            {
                return new AuthFileInspection(hasUsableAuthFile, hasEmbeddedApiKey, string.Empty);
            }

            var payload = ParseJwtPayload(idToken!);
            var accountEmail = FirstNonEmptyString(
                payload.TryGetValue("email", out var email) ? email?.ToString() : null,
                payload.TryGetValue("preferred_username", out var preferredUsername) ? preferredUsername?.ToString() : null,
                tokens?["account_id"]?.ToString());

            return new AuthFileInspection(hasUsableAuthFile, hasEmbeddedApiKey, accountEmail);
        }
        catch
        {
            return AuthFileInspection.Empty;
        }
    }

    private static string FirstNonEmptyString(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static JObject ParseJwtPayload(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
        {
            return new JObject();
        }

        var payloadBytes = DecodeBase64Url(parts[1]);
        var payloadJson = Encoding.UTF8.GetString(payloadBytes);
        return JObject.Parse(payloadJson);
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        var padding = 4 - (normalized.Length % 4);
        if (padding is > 0 and < 4)
        {
            normalized = normalized.PadRight(normalized.Length + padding, '=');
        }

        return Convert.FromBase64String(normalized);
    }

    private static string QuoteForCommandShell(string value)
    {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static bool RequiresCommandShell(string executablePath)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            return false;
        }

        var extension = Path.GetExtension(executablePath);
        return string.IsNullOrEmpty(extension)
            || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPowerShellScript(string executablePath)
    {
        return Environment.OSVersion.Platform == PlatformID.Win32NT
            && Path.GetExtension(executablePath).Equals(".ps1", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryTerminateProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(1000);
            }
        }
        catch
        {
        }
    }

    private static string ResolvePowerShellHost()
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var windowsPowerShell = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        return File.Exists(windowsPowerShell)
            ? windowsPowerShell
            : "powershell.exe";
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
    }

    private sealed class AuthFileInspection
    {
        public static AuthFileInspection Empty { get; } = new(false, false, string.Empty);

        public AuthFileInspection(bool hasUsableAuthFile, bool hasEmbeddedApiKey, string accountEmail)
        {
            HasUsableAuthFile = hasUsableAuthFile;
            HasEmbeddedApiKey = hasEmbeddedApiKey;
            AccountEmail = accountEmail ?? string.Empty;
        }

        public bool HasUsableAuthFile { get; }

        public bool HasEmbeddedApiKey { get; }

        public string AccountEmail { get; }
    }
}
