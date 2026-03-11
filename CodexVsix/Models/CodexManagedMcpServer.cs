namespace CodexVsix.Models;

public sealed class CodexManagedMcpServer
{
    public bool Enabled { get; set; } = true;

    public string Name { get; set; } = string.Empty;

    public string TransportType { get; set; } = "stdio";

    public string Command { get; set; } = string.Empty;

    public string Arguments { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;
}
