using System.Collections.Generic;

namespace CodexVsix.Models;

public sealed class CodexApprovalRequest
{
    public string Method { get; set; } = string.Empty;

    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;

    public string ItemId { get; set; } = string.Empty;

    public string? ApprovalId { get; set; }

    public string? Command { get; set; }

    public string? WorkingDirectory { get; set; }

    public string? Reason { get; set; }

    public string? GrantRoot { get; set; }

    public string? ProposedExecpolicyLabel { get; set; }

    public IReadOnlyList<CodexApprovalOption> Options { get; set; } = new List<CodexApprovalOption>();
}
