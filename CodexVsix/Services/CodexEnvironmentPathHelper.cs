using System;
using System.Collections.Generic;
using System.IO;

namespace CodexVsix.Services;

internal static class CodexEnvironmentPathHelper
{
    public static string GetCodexHomeDirectory(string? environmentVariables = null)
    {
        var configuredHome = GetEffectiveEnvironmentVariable("CODEX_HOME", environmentVariables);
        if (!string.IsNullOrWhiteSpace(configuredHome))
        {
            return NormalizePath(configuredHome);
        }

        return Path.Combine(GetUserHomeDirectory(environmentVariables), ".codex");
    }

    public static string GetUserHomeDirectory(string? environmentVariables = null)
    {
        var home = FirstNonEmpty(
            GetEffectiveEnvironmentVariable("HOME", environmentVariables),
            GetEffectiveEnvironmentVariable("USERPROFILE", environmentVariables));

        if (!string.IsNullOrWhiteSpace(home))
        {
            return NormalizePath(home);
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public static string GetEffectiveEnvironmentVariable(string key, string? environmentVariables = null)
    {
        var overrides = ParseEnvironmentVariables(environmentVariables);
        if (overrides.TryGetValue(key, out var overrideValue) && !string.IsNullOrWhiteSpace(overrideValue))
        {
            return overrideValue;
        }

        return Environment.GetEnvironmentVariable(key) ?? string.Empty;
    }

    public static IReadOnlyDictionary<string, string> ParseEnvironmentVariables(string? environmentVariables)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in (environmentVariables ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line.Substring(0, separatorIndex).Trim();
            var value = line.Substring(separatorIndex + 1).Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                values[key] = value;
            }
        }

        return values;
    }

    private static string NormalizePath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables((path ?? string.Empty).Trim().Trim('"'));
        if (string.IsNullOrWhiteSpace(expanded))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(expanded);
        }
        catch
        {
            return expanded;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value!;
            }
        }

        return string.Empty;
    }
}
