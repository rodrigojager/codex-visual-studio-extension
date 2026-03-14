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
    public const string DefaultInstallCommand = "npm install -g @openai/codex";

    public async Task<CodexEnvironmentStatus> InspectAsync(CodexExtensionSettings settings, CancellationToken cancellationToken)
    {
        var configuredExecutablePath = ResolveConfiguredExecutablePath(settings.CodexExecutablePath);
        var status = new CodexEnvironmentStatus
        {
            Stage = CodexSetupStage.Checking,
            ConfiguredExecutablePath = configuredExecutablePath,
            AuthFilePath = GetAuthFilePath()
        };

        try
        {
            var resolvedExecutablePath = ResolveExecutableLocation(configuredExecutablePath);
            if (string.IsNullOrWhiteSpace(resolvedExecutablePath))
            {
                status.Stage = CodexSetupStage.MissingExecutable;
                return status;
            }

            status.ResolvedExecutablePath = resolvedExecutablePath;

            var version = await TryGetVersionAsync(resolvedExecutablePath, cancellationToken).ConfigureAwait(false);
            if (!version.Success)
            {
                status.Stage = CodexSetupStage.Error;
                status.ErrorDetail = version.Detail;
                return status;
            }

            status.Version = version.Detail;
            status.HasApiKey = HasApiKey(settings.EnvironmentVariables) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
            status.HasAuthFile = HasUsableAuthFile(status.AuthFilePath);
            status.AccountEmail = status.HasAuthFile ? TryReadAccountEmail(status.AuthFilePath) : string.Empty;

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

    public string GetAuthFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "auth.json");
    }

    private static string ResolveConfiguredExecutablePath(string? configuredPath)
    {
        var trimmed = string.IsNullOrWhiteSpace(configuredPath)
            ? "codex.cmd"
            : configuredPath.Trim();

        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            return trimmed;
        }

        return string.Equals(trimmed, "codex", StringComparison.OrdinalIgnoreCase)
            ? "codex.cmd"
            : trimmed;
    }

    private static string ResolveExecutableLocation(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        if ((configuredPath.Contains("\\") || configuredPath.Contains("/")) && File.Exists(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathEntries = pathValue.Split(Path.PathSeparator)
            .Where(entry => !string.IsNullOrWhiteSpace(entry));

        var candidateNames = BuildCandidateNames(configuredPath);
        foreach (var directory in pathEntries)
        {
            var normalizedDirectory = directory.Trim();
            foreach (var candidateName in candidateNames)
            {
                var candidatePath = Path.Combine(normalizedDirectory, candidateName);
                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }
        }

        return string.Empty;
    }

    private static string[] BuildCandidateNames(string configuredPath)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            return new[] { configuredPath };
        }

        var extension = Path.GetExtension(configuredPath);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return new[] { configuredPath };
        }

        return new[]
        {
            configuredPath,
            configuredPath + ".cmd",
            configuredPath + ".bat",
            configuredPath + ".exe"
        };
    }

    private static async Task<(bool Success, string Detail)> TryGetVersionAsync(string executablePath, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            if (!process.Start())
            {
                return (false, "Não foi possível iniciar o Codex CLI.");
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
            return (false, "O Codex CLI demorou demais para responder ao comando de versão.");
        }

        var output = await outputTask.ConfigureAwait(false);
        var errorText = await errorTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(errorText) ? output : errorText;
            return (false, string.IsNullOrWhiteSpace(error) ? "Não foi possível obter a versão do Codex." : error.Trim());
        }

        var versionText = FirstNonEmptyLine(output);
        return (true, string.IsNullOrWhiteSpace(versionText) ? "Codex detectado" : versionText);
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

    private static string TryReadAccountEmail(string authFilePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(authFilePath) || !File.Exists(authFilePath))
            {
                return string.Empty;
            }

            var document = JObject.Parse(File.ReadAllText(authFilePath));
            var tokens = document["tokens"] as JObject;
            var idToken = tokens?["id_token"]?.ToString();
            if (string.IsNullOrWhiteSpace(idToken))
            {
                return string.Empty;
            }

            var payload = ParseJwtPayload(idToken);
            return FirstNonEmptyString(
                payload.TryGetValue("email", out var email) ? email?.ToString() : null,
                payload.TryGetValue("preferred_username", out var preferredUsername) ? preferredUsername?.ToString() : null,
                tokens?["account_id"]?.ToString());
        }
        catch
        {
            return string.Empty;
        }
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

    private static string FirstNonEmptyString(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static bool HasUsableAuthFile(string authFilePath)
    {
        if (string.IsNullOrWhiteSpace(authFilePath) || !File.Exists(authFilePath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(authFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            var auth = JObject.Parse(json);
            var embeddedApiKey = auth["OPENAI_API_KEY"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(embeddedApiKey))
            {
                return true;
            }

            var accessToken = auth["tokens"]?["access_token"]?.Value<string>();
            var refreshToken = auth["tokens"]?["refresh_token"]?.Value<string>();
            return !string.IsNullOrWhiteSpace(accessToken) || !string.IsNullOrWhiteSpace(refreshToken);
        }
        catch
        {
            return false;
        }
    }

    private static string QuoteForCommandShell(string value)
    {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
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
}
