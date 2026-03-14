namespace CodexVsix.Models;

public sealed class CodexRateLimitSummary
{
    public string PlanLabel { get; set; } = string.Empty;

    public CodexRateLimitWindowSummary PrimaryWindow { get; set; } = new();

    public CodexRateLimitWindowSummary SecondaryWindow { get; set; } = new();

    public string CreditsLabel { get; set; } = string.Empty;

    public bool HasPrimaryWindow => PrimaryWindow.HasData;

    public bool HasSecondaryWindow => SecondaryWindow.HasData;

    public bool HasCredits => !string.IsNullOrWhiteSpace(CreditsLabel);

    public bool HasAnyData => HasPrimaryWindow || HasSecondaryWindow || HasCredits || !string.IsNullOrWhiteSpace(PlanLabel);
}

public sealed class CodexRateLimitWindowSummary
{
    public string Title { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public bool HasData => !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Detail);
}
