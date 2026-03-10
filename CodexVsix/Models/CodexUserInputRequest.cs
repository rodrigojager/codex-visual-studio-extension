using System.Collections.Generic;

namespace CodexVsix.Models;

public sealed class CodexUserInputRequest
{
    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;

    public string ItemId { get; set; } = string.Empty;

    public IReadOnlyList<CodexUserInputQuestion> Questions { get; set; } = new List<CodexUserInputQuestion>();
}
