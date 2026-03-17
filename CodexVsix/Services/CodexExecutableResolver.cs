using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CodexVsix.Services;

internal static class CodexExecutableResolver
{
    public static string NormalizeConfiguredExecutablePath(string? configuredPath)
    {
        var trimmed = string.IsNullOrWhiteSpace(configuredPath)
            ? DefaultWindowsExecutableName()
            : configuredPath.Trim();

        if (!IsWindows())
        {
            return trimmed;
        }

        return string.Equals(trimmed, "codex", StringComparison.OrdinalIgnoreCase)
            ? DefaultWindowsExecutableName()
            : trimmed;
    }

    public static string ResolveExecutableLocation(string? configuredPath, string? environmentVariables)
    {
        var normalizedConfiguredPath = NormalizeConfiguredExecutablePath(configuredPath);
        if (string.IsNullOrWhiteSpace(normalizedConfiguredPath))
        {
            return string.Empty;
        }

        var directMatch = ResolveDirectPath(normalizedConfiguredPath);
        if (!string.IsNullOrWhiteSpace(directMatch))
        {
            return directMatch;
        }

        if (LooksLikePath(normalizedConfiguredPath))
        {
            return string.Empty;
        }

        var fromPath = ResolveFromPath(normalizedConfiguredPath, environmentVariables);
        if (!string.IsNullOrWhiteSpace(fromPath))
        {
            return fromPath;
        }

        var fromCommandResolution = ResolveWithCommandResolution(normalizedConfiguredPath, environmentVariables);
        if (!string.IsNullOrWhiteSpace(fromCommandResolution))
        {
            return fromCommandResolution;
        }

        return string.Empty;
    }

    private static string ResolveDirectPath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        if ((configuredPath.Contains("\\") || configuredPath.Contains("/")) && File.Exists(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        return string.Empty;
    }

    private static string ResolveFromPath(string configuredPath, string? environmentVariables)
    {
        var pathEntries = GetPathSearchEntries(environmentVariables)
            .Where(entry => !string.IsNullOrWhiteSpace(entry));

        var candidateNames = BuildCandidateNames(configuredPath);
        foreach (var directory in pathEntries)
        {
            var normalizedDirectory = directory.Trim().Trim('"');
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

    private static IEnumerable<string> GetPathSearchEntries(string? environmentVariables)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ExpandPathEntries(CodexEnvironmentPathHelper.GetEffectiveEnvironmentVariable("PATH", environmentVariables)))
        {
            if (seen.Add(entry))
            {
                yield return entry;
            }
        }

        if (!IsWindows())
        {
            yield break;
        }

        foreach (var entry in ExpandPathEntries(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? string.Empty))
        {
            if (seen.Add(entry))
            {
                yield return entry;
            }
        }

        foreach (var entry in ExpandPathEntries(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? string.Empty))
        {
            if (seen.Add(entry))
            {
                yield return entry;
            }
        }
    }

    private static IEnumerable<string> ExpandPathEntries(string pathValue)
    {
        foreach (var entry in (pathValue ?? string.Empty).Split(Path.PathSeparator))
        {
            var normalized = entry.Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                yield return normalized;
            }
        }
    }

    private static string[] BuildCandidateNames(string configuredPath)
    {
        if (!IsWindows())
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
            configuredPath + ".exe",
            configuredPath + ".ps1"
        };
    }

    private static string ResolveWithCommandResolution(string configuredPath, string? environmentVariables)
    {
        if (!IsWindows())
        {
            return string.Empty;
        }

        var commandHost = ResolvePowerShellHost();
        if (string.IsNullOrWhiteSpace(commandHost))
        {
            return string.Empty;
        }

        var psi = new ProcessStartInfo
        {
            FileName = commandHost,
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command " + QuotePowerShellArgument(
                "$cmd = Get-Command -Name " + QuotePowerShellLiteral(configuredPath) + " -ErrorAction Stop | Select-Object -First 1; " +
                "$path = if ($cmd.Path) { $cmd.Path } elseif ($cmd.Source -and ($cmd.CommandType -eq 'Application' -or $cmd.CommandType -eq 'ExternalScript' -or $cmd.CommandType -eq 'Script')) { $cmd.Source } else { '' }; " +
                "if ($path) { [Console]::Out.Write($path) }"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        ApplyEnvironmentVariables(psi, environmentVariables);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return string.Empty;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(3000);
            return process.ExitCode == 0 && File.Exists(output) ? output : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool LooksLikePath(string configuredPath)
    {
        return Path.IsPathRooted(configuredPath)
            || configuredPath.Contains("\\")
            || configuredPath.Contains("/");
    }

    private static void ApplyEnvironmentVariables(ProcessStartInfo psi, string? environmentVariables)
    {
        foreach (var entry in CodexEnvironmentPathHelper.ParseEnvironmentVariables(environmentVariables))
        {
            psi.EnvironmentVariables[entry.Key] = entry.Value;
        }
    }

    private static string ResolvePowerShellHost()
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var windowsPowerShell = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(windowsPowerShell))
        {
            return windowsPowerShell;
        }

        return "powershell.exe";
    }

    private static string QuotePowerShellLiteral(string value)
    {
        return "'" + (value ?? string.Empty).Replace("'", "''") + "'";
    }

    private static string QuotePowerShellArgument(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
    }

    private static bool IsWindows()
    {
        return Environment.OSVersion.Platform == PlatformID.Win32NT;
    }

    private static string DefaultWindowsExecutableName()
    {
        return IsWindows() ? "codex.cmd" : "codex";
    }
}
