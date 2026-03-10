using System;

namespace CodexVsix.Models;

public sealed class CodexThreadSummary
{
    public string ThreadId { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string Preview { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }

    public string Status { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public string Title => string.IsNullOrWhiteSpace(Name) ? Preview : Name!;

    public string Subtitle => string.IsNullOrWhiteSpace(Name) ? string.Empty : Preview;
}
