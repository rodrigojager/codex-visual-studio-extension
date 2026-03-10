using System;
using Microsoft.VisualStudio.Shell;

namespace CodexVsix;

public sealed class CodexToolWindow : ToolWindowPane
{
    public CodexToolWindow() : base(null)
    {
        Caption = "Codex";
        Content = new CodexToolWindowControl();
    }
}
