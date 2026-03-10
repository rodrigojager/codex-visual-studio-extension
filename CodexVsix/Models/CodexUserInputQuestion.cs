using System.Collections.Generic;

namespace CodexVsix.Models;

public sealed class CodexUserInputQuestion
{
    public string Header { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public string Question { get; set; } = string.Empty;

    public bool IsOther { get; set; }

    public bool IsSecret { get; set; }

    public IReadOnlyList<CodexUserInputOption> Options { get; set; } = new List<CodexUserInputOption>();
}
