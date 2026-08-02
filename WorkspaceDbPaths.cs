#nullable enable

namespace CdpMcp;

/// <summary>
/// Per-seat WitDB paths — dual seats must not share one <c>FileShare.None</c> file.
/// Pattern matches pressure/ignite under <c>StateRoot/{seat}/</c>.
/// </summary>
internal static class WorkspaceDbPaths
{
    public const string FileName = "intent-workspace.witdb";
    public const string PrimarySeat = "cdp";

    public static string LegacyPath(string stateRoot) =>
        Path.Combine(stateRoot, FileName);

    public static string SeatPath(string stateRoot, string seat) =>
        Path.Combine(stateRoot, seat, FileName);

    /// <summary>
    /// Resolve DB path. Explicit override wins. Else seat-local file;
    /// primary seat once inherits legacy shared file via Move.
    /// </summary>
    public static string Resolve(string? pathOverride, string stateRoot, string seat)
    {
        if (!string.IsNullOrWhiteSpace(pathOverride))
            return Path.GetFullPath(pathOverride.Trim());

        var seatNorm = string.IsNullOrWhiteSpace(seat) ? PrimarySeat : seat.Trim();
        var seatPath = SeatPath(stateRoot, seatNorm);
        TryMigrateLegacyToPrimary(seatPath, LegacyPath(stateRoot), seatNorm);
        return seatPath;
    }

    /// <summary>
    /// One-time: <c>StateRoot/intent-workspace.witdb</c> → <c>StateRoot/cdp/…</c>.
    /// Sibling seats start empty (no 442MB copy).
    /// </summary>
    public static bool TryMigrateLegacyToPrimary(string seatPath, string legacyPath, string seat)
    {
        if (File.Exists(seatPath))
            return false;
        if (!File.Exists(legacyPath))
            return false;
        if (!string.Equals(seat, PrimarySeat, StringComparison.OrdinalIgnoreCase))
            return false;

        var dir = Path.GetDirectoryName(seatPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.Move(legacyPath, seatPath);
        return true;
    }
}
