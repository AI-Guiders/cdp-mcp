#nullable enable
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;

namespace CdpMcp;

/// <summary>
/// Seat WitDB torn free-list / pageNumber OOR (historical dual-seat FileShare fights).
/// Quarantine to <c>*.torn-*.bak</c> and EnsureCreated a fresh seat file.
/// </summary>
internal static class WorkspaceDbTornHeal
{
    public static bool IsTornPageException(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is ArgumentOutOfRangeException aor
                && string.Equals(aor.ParamName, "pageNumber", StringComparison.Ordinal))
                return true;

            var m = e.Message;
            if (m.Contains("Page number", StringComparison.Ordinal)
                && m.Contains("out of range", StringComparison.OrdinalIgnoreCase))
                return true;

            if (m.Contains("FreePage", StringComparison.OrdinalIgnoreCase)
                && m.Contains("TotalPageCount", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Move seat DB (+ indexes sidecar) aside; return bak path of the main file.</summary>
    public static string Quarantine(string databasePath)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var bak = databasePath + $".torn-{stamp}.bak";
        for (var n = 0; File.Exists(bak); n++)
            bak = databasePath + $".torn-{stamp}-{n}.bak";

        if (File.Exists(databasePath))
            File.Move(databasePath, bak);

        QuarantineSidecar(databasePath + "_indexes", stamp);
        return bak;
    }

    public static string QuarantineAndRecreate(
        DbContextOptions<IntentWorkspaceDbContext> options,
        string databasePath)
    {
        var bak = Quarantine(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        using var boot = new IntentWorkspaceDbContext(options);
        boot.Database.EnsureCreated();
        return bak;
    }

    static void QuarantineSidecar(string sidecarPath, string stamp)
    {
        if (!File.Exists(sidecarPath) && !Directory.Exists(sidecarPath))
            return;

        var bak = sidecarPath + $".torn-{stamp}.bak";
        for (var n = 0; File.Exists(bak) || Directory.Exists(bak); n++)
            bak = sidecarPath + $".torn-{stamp}-{n}.bak";

        if (File.Exists(sidecarPath))
            File.Move(sidecarPath, bak);
        else
            Directory.Move(sidecarPath, bak);
    }
}
