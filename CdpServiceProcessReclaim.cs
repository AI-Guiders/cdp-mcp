using System.Diagnostics;

namespace CdpMcp;

/// <summary>
/// Reclaim stale CdpService on same exe path (deploy hot-swap). Bridge exe is separate.
/// </summary>
internal static class CdpServiceProcessReclaim
{
    public static void Ensure()
    {
        if (IdeSeatProcessReclaim.IsSkipEnabled())
            return;

        string? selfExe;
        try
        {
            selfExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(selfExe))
            return;

        var selfId = Environment.ProcessId;
        foreach (var pid in IdeSeatProcessReclaim.CollectOtherSameExePids(selfExe, selfId))
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited)
                    continue;
                p.Kill(entireProcessTree: true);
            }
            catch
            {
                /* best-effort */
            }
        }
    }
}
