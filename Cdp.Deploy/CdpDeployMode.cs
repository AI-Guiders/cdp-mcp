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
    public static bool TryParse(string? raw, out CdpDeployMode mode)
    {
        switch ((raw ?? "").Trim().ToLowerInvariant())
        {
            case "soft" or "s" or "stage":
                mode = CdpDeployMode.Soft;
                return true;
            case "hard" or "h" or "kill":
                mode = CdpDeployMode.Hard;
                return true;
            case "apply" or "a" or "pending" or "apply_pending":
                mode = CdpDeployMode.Apply;
                return true;
            case "rollout" or "r" or "dual":
                mode = CdpDeployMode.Rollout;
                return true;
            case "ship" or "sh" or "slot":
                mode = CdpDeployMode.Ship;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    /// <summary>Unknown tokens default to ship (ADR-0226 routine path).</summary>
    public static CdpDeployMode Parse(string? raw) =>
        TryParse(raw, out var mode) ? mode : CdpDeployMode.Ship;

    public static string ToWire(this CdpDeployMode mode) => mode switch
    {
        CdpDeployMode.Soft => "soft",
        CdpDeployMode.Hard => "hard",
        CdpDeployMode.Apply => "apply",
        CdpDeployMode.Rollout => "rollout",
        CdpDeployMode.Ship => "ship",
        _ => "ship"
    };
}
