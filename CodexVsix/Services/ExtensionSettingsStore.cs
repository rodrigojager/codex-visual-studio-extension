using System;
using System.IO;
using CodexVsix.Models;
using Newtonsoft.Json;

namespace CodexVsix.Services;

public sealed class ExtensionSettingsStore
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexVsix");

    private static readonly string SettingsFile = Path.Combine(SettingsDirectory, "settings.json");

    public CodexExtensionSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFile))
            {
                return new CodexExtensionSettings();
            }

            var json = File.ReadAllText(SettingsFile);
            var settings = JsonConvert.DeserializeObject<CodexExtensionSettings>(json) ?? new CodexExtensionSettings();
            settings.PromptHistory ??= new();
            settings.CodexExecutablePath ??= "codex.cmd";
            settings.LanguageOverride ??= "";
            settings.WorkingDirectory ??= "";
            settings.DefaultModel ??= "";
            settings.ReasoningEffort ??= "";
            settings.ModelVerbosity ??= "";
            settings.ServiceTier ??= "";
            settings.Profile ??= "";
            settings.ApprovalPolicy ??= "";
            settings.SandboxMode ??= "";
            settings.AdditionalArguments ??= "";
            settings.EnvironmentVariables ??= "";
            settings.RawTomlOverrides ??= "";
            settings.CurrentThreadId ??= "";
            settings.LastThreadWorkingDirectory ??= "";
            settings.ManagedMcpServers ??= new();
            settings.PreferredMcpServers ??= new();
            return settings;
        }
        catch
        {
            return new CodexExtensionSettings();
        }
    }

    public void Save(CodexExtensionSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
        File.WriteAllText(SettingsFile, json);
    }
}
