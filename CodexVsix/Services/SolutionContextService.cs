using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace CodexVsix.Services;

public sealed class SolutionContextService
{
    public string? TryGetBestWorkspaceDirectory()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return TryGetSolutionDirectory()
            ?? TryGetActiveProjectDirectory()
            ?? TryGetFirstProjectDirectory()
            ?? TryGetActiveDocumentDirectory();
    }

    public string? TryGetSolutionDirectory()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
        var solutionPath = dte?.Solution?.FullName;
        if (!string.IsNullOrWhiteSpace(solutionPath) && File.Exists(solutionPath))
        {
            return Path.GetDirectoryName(solutionPath);
        }

        return null;
    }

    public string GetBestWorkingDirectory()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return TryGetBestWorkspaceDirectory() ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
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
        return Path.Combine(GetCodexHomeDirectory(), "config.toml");
    }

    public string GetCodexHomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".codex");
    }

    public string GetCodexSkillsDirectory()
    {
        return Path.Combine(GetCodexHomeDirectory(), "skills");
    }

    public void OpenCodexConfig()
    {
        var path = GetCodexConfigPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, "model = \"gpt-5.4\"" + Environment.NewLine);
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    public void OpenCodexSkillsDirectory()
    {
        var path = GetCodexSkillsDirectory();
        Directory.CreateDirectory(path);
        OpenPath(path);
    }

    public void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        System.Diagnostics.Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    public string CreateSkillTemplate(string skillName, string description)
    {
        var normalizedSkillName = NormalizeSkillName(skillName);
        var skillDirectory = Path.Combine(GetCodexSkillsDirectory(), normalizedSkillName);
        Directory.CreateDirectory(skillDirectory);

        var skillFile = Path.Combine(skillDirectory, "SKILL.md");
        if (!File.Exists(skillFile))
        {
            File.WriteAllText(
                skillFile,
                BuildSkillTemplate(normalizedSkillName, description),
                new UTF8Encoding(false));
        }

        return skillFile;
    }

    public static bool IsValidSkillName(string? skillName)
    {
        if (string.IsNullOrWhiteSpace(skillName))
        {
            return false;
        }

        var trimmed = skillName.Trim();
        if (!char.IsLetterOrDigit(trimmed[0]))
        {
            return false;
        }

        return trimmed.All(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-');
    }

    private static string NormalizeSkillName(string skillName)
    {
        var trimmed = (skillName ?? string.Empty).Trim();
        if (!IsValidSkillName(trimmed))
        {
            throw new ArgumentException("Nome de skill inválido.", nameof(skillName));
        }

        return trimmed;
    }

    private static string BuildSkillTemplate(string skillName, string description)
    {
        var summary = string.IsNullOrWhiteSpace(description)
            ? "Descreva aqui quando usar esta skill e qual problema ela resolve."
            : description.Trim();

        return "# " + skillName + Environment.NewLine
            + Environment.NewLine
            + summary + Environment.NewLine
            + Environment.NewLine
            + "## Quando usar" + Environment.NewLine
            + "- Explique em quais pedidos essa skill deve ser acionada." + Environment.NewLine
            + Environment.NewLine
            + "## Fluxo" + Environment.NewLine
            + "1. Descreva o passo inicial." + Environment.NewLine
            + "2. Liste as validações ou cuidados." + Environment.NewLine
            + "3. Finalize com o resultado esperado." + Environment.NewLine;
    }

    private static bool IsIgnoredPath(string fullPath)
    {
        var p = fullPath.Replace('\\', '/');
        return p.Contains("/.git/") || p.Contains("/bin/") || p.Contains("/obj/") || p.Contains("/node_modules/") || p.Contains("/.vs/");
    }

    private static string? TryGetActiveProjectDirectory()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
            if (dte?.ActiveSolutionProjects is not Array activeProjects)
            {
                return null;
            }

            foreach (var entry in activeProjects)
            {
                if (entry is Project project)
                {
                    var directory = TryGetProjectDirectory(project);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        return directory;
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? TryGetFirstProjectDirectory()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
            var projects = dte?.Solution?.Projects;
            if (projects is null)
            {
                return null;
            }

            foreach (Project project in projects)
            {
                var directory = TryGetProjectDirectory(project);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    return directory;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? TryGetActiveDocumentDirectory()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
            var fullName = dte?.ActiveDocument?.FullName;
            if (!string.IsNullOrWhiteSpace(fullName) && File.Exists(fullName))
            {
                return Path.GetDirectoryName(fullName);
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? TryGetProjectDirectory(Project? project)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (project is null)
        {
            return null;
        }

        try
        {
            var fullName = project.FullName;
            if (!string.IsNullOrWhiteSpace(fullName))
            {
                if (File.Exists(fullName))
                {
                    return Path.GetDirectoryName(fullName);
                }

                if (Directory.Exists(fullName))
                {
                    return fullName;
                }
            }
        }
        catch
        {
        }

        try
        {
            var fullPath = project.Properties?.Item("FullPath")?.Value as string;
            if (!string.IsNullOrWhiteSpace(fullPath) && Directory.Exists(fullPath))
            {
                return fullPath;
            }
        }
        catch
        {
        }

        try
        {
            if (project.ProjectItems is null)
            {
                return null;
            }

            foreach (ProjectItem item in project.ProjectItems)
            {
                var nested = TryGetProjectDirectory(item.SubProject);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        catch
        {
        }

        return null;
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
