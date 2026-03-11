namespace CodexVsix.Models;

public enum CodexSetupStage
{
    Unknown = 0,
    Checking = 1,
    MissingExecutable = 2,
    MissingAuthentication = 3,
    Ready = 4,
    Error = 5
}
