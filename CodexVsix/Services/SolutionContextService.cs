using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace CodexVsix.Services;

public sealed class SolutionContextService
{
    public string GetBestWorkingDirectory()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
        var solutionPath = dte?.Solution?.FullName;
        if (!string.IsNullOrWhiteSpace(solutionPath) && File.Exists(solutionPath))
        {
            return Path.GetDirectoryName(solutionPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public IReadOnlyList<string> FindSolutionFiles(string search)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var root = GetBestWorkingDirectory();
        if (!Directory.Exists(root))
        {
            return Array.Empty<string>();
        }

        var normalized = (search ?? string.Empty).Trim().Replace('\\', '/');
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !IsIgnoredPath(f))
            .Select(f => MakeRelative(root, f))
            .Where(f => string.IsNullOrWhiteSpace(normalized) || f.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(f => Score(f, normalized))
            .ThenBy(f => f)
            .Take(30)
            .ToList();

        return files;
    }

    public string GetCodexConfigPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".codex", "config.toml");
    }

    public void OpenCodexConfig()
    {
        var path = GetCodexConfigPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, "model = \"gpt-5-codex\"" + Environment.NewLine);
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private static bool IsIgnoredPath(string fullPath)
    {
        var p = fullPath.Replace('\\', '/');
        return p.Contains("/.git/") || p.Contains("/bin/") || p.Contains("/obj/") || p.Contains("/node_modules/") || p.Contains("/.vs/");
    }

    private static string MakeRelative(string root, string file)
    {
        var relative = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return relative.Replace('\\', '/');
    }

    private static int Score(string file, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return int.MaxValue / 2;
        }

        var idx = file.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        return idx < 0 ? int.MaxValue : idx * 10 + file.Length;
    }
}
