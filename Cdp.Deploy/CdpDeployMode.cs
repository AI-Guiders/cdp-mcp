namespace Cdp.Deploy;

public enum CdpDeployMode
{
    Soft,
    Hard,
    Apply,
    Rollout
}

public static class CdpDeployModeParser
{
    public static CdpDeployMode Parse(string? raw) =>
        (raw ?? "").Trim().ToLowerInvariant() switch
        {
            "soft" or "s" or "stage" => CdpDeployMode.Soft,
            "hard" or "h" or "kill" => CdpDeployMode.Hard,
            "apply" or "a" or "pending" or "apply_pending" => CdpDeployMode.Apply,
            "rollout" or "r" or "dual" => CdpDeployMode.Rollout,
            _ => CdpDeployMode.Hard
        };
}
