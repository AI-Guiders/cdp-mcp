namespace Cdp.Deploy;

public enum CdpDeployMode
{
    Soft,
    Hard,
    Apply,
    Rollout,
    /// <summary>ADR-0209 stage 3: publish → immutable snapshot → start slot from it → retire old. No promote over a live root.</summary>
    Ship
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
            "ship" or "sh" or "slot" => CdpDeployMode.Ship,
            _ => CdpDeployMode.Hard
        };
}
