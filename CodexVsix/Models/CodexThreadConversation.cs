using System.Collections.Generic;

namespace CodexVsix.Models;

public sealed class CodexThreadConversation
{
    public CodexThreadSummary Thread { get; set; } = new CodexThreadSummary();

    public IReadOnlyList<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
