using System.Collections.Generic;

namespace CodexVsix.Models;

public sealed class CodexExtensionSettings
{
    public string CodexExecutablePath { get; set; } = "codex.cmd";
    public string LanguageOverride { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string DefaultModel { get; set; } = "";
    public string ReasoningEffort { get; set; } = "";
    public string ModelVerbosity { get; set; } = "";
    public string ServiceTier { get; set; } = "";
    public string Profile { get; set; } = "";
    public string ApprovalPolicy { get; set; } = "";
    public string SandboxMode { get; set; } = "";
    public string AdditionalArguments { get; set; } = "";
    public string EnvironmentVariables { get; set; } = "";
    public string RawTomlOverrides { get; set; } = "";
    public string CurrentThreadId { get; set; } = "";
    public string LastThreadWorkingDirectory { get; set; } = "";
    public List<string> PromptHistory { get; set; } = new();
    public List<CodexManagedMcpServer> ManagedMcpServers { get; set; } = new();
    public List<string> PreferredMcpServers { get; set; } = new();
    public bool StreamOutput { get; set; } = true;
    public bool ReuseSession { get; set; } = false;
    public bool AutoApprovePowerShell { get; set; } = false;
    public bool PlanModeEnabled { get; set; } = false;
    public bool IncludeIdeContext { get; set; } = true;
}
