using Newtonsoft.Json.Linq;

namespace CodexVsix.Models;

public sealed class ApprovalOptionViewModel
{
    public ApprovalOptionViewModel(string label, JToken decision, bool isPrimary = false, bool isDanger = false)
    {
        Label = label;
        Decision = decision;
        IsPrimary = isPrimary;
        IsDanger = isDanger;
    }

    public string Label { get; }

    public JToken Decision { get; }

    public bool IsPrimary { get; }

    public bool IsDanger { get; }
}
