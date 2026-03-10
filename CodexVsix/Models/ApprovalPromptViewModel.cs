using System.Collections.ObjectModel;

namespace CodexVsix.Models;

public sealed class ApprovalPromptViewModel
{
    public string Title { get; set; } = string.Empty;

    public string? Subtitle { get; set; }

    public string? Command { get; set; }

    public string? WorkingDirectory { get; set; }

    public string? Reason { get; set; }

    public string? GrantRoot { get; set; }

    public ObservableCollection<ApprovalOptionViewModel> Options { get; } = new();
}
