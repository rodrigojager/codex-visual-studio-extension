namespace CodexVsix.Models;

public sealed class CodexEnvironmentStatus
{
    public CodexSetupStage Stage { get; set; } = CodexSetupStage.Unknown;

    public string ConfiguredExecutablePath { get; set; } = string.Empty;

    public string ResolvedExecutablePath { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string AuthFilePath { get; set; } = string.Empty;

    public bool HasAuthFile { get; set; }

    public bool HasApiKey { get; set; }

    public bool RequiresOpenaiAuth { get; set; } = true;

    public string AccountEmail { get; set; } = string.Empty;

    public string AuthenticationLabel { get; set; } = string.Empty;

    public string ErrorDetail { get; set; } = string.Empty;

    public bool IsReady => Stage == CodexSetupStage.Ready;

    public bool HasAccountEmail => !string.IsNullOrWhiteSpace(AccountEmail);
}
