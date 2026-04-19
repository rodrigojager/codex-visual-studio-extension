using System;
using System.Collections.Generic;
using System.Linq;

namespace CodexVsix.Models;

public sealed class CodexRateLimitSummary
{
    public IReadOnlyList<CodexRateLimitWindowSummary> Entries { get; set; } = Array.Empty<CodexRateLimitWindowSummary>();

    public bool HasAnyData => Entries.Any(entry => entry.HasData);
}

public sealed class CodexRateLimitWindowSummary
{
    public string Title { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public bool HasData => !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Detail);
}
