namespace CodexVsix.Models;

public sealed class CodexSkillSummary
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public bool IsSystem { get; set; }

    public string ScopeLabel { get; set; } = string.Empty;
}
