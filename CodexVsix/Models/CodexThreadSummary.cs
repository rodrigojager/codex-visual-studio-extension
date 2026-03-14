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

    public string UpdatedAtLabel
    {
        get
        {
            var delta = DateTimeOffset.Now - UpdatedAt.ToLocalTime();
            if (delta.TotalMinutes < 1)
            {
                return "now";
            }

            if (delta.TotalHours < 1)
            {
                return Math.Max(1, (int)Math.Floor(delta.TotalMinutes)) + "m";
            }

            if (delta.TotalDays < 1)
            {
                return Math.Max(1, (int)Math.Floor(delta.TotalHours)) + "h";
            }

            return Math.Max(1, (int)Math.Floor(delta.TotalDays)) + "d";
        }
    }
}
