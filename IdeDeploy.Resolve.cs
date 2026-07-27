#nullable enable
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdeDeploy
{
    internal static string? ResolveSelfInstallRoot()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetDirectoryName(Path.GetFullPath(exe));
    }

    internal static string ClassifySeat(string? selfRoot)
    {
        if (SamePath(selfRoot, ReleaseTarget))
            return "cdp";
        if (SamePath(selfRoot, DebugTarget))
            return "cdp-debug";
        return "other";
    }

    internal readonly record struct TargetDecision(
        bool Ok,
        string? Target,
        string? Sibling,
        string? Error,
        string? Hint);

    internal static TargetDecision ResolveTarget(
        string? selfRoot,
        string seat,
        string? targetRaw,
        string mode,
        bool force)
    {
        var sibling = seat switch
        {
            "cdp" => DebugTarget,
            "cdp-debug" => ReleaseTarget,
            _ => ReleaseTarget
        };

        string target;
        var raw = (targetRaw ?? "").Trim();
        if (raw.Length == 0 || raw.Equals("sibling", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("other", StringComparison.OrdinalIgnoreCase))
        {
            target = sibling;
        }
        else if (raw.Equals("self", StringComparison.OrdinalIgnoreCase)
                 || raw.Equals("here", StringComparison.OrdinalIgnoreCase))
        {
            target = selfRoot ?? ReleaseTarget;
        }
        else if (raw.Equals("release", StringComparison.OrdinalIgnoreCase)
                 || raw.Equals("cdp", StringComparison.OrdinalIgnoreCase))
        {
            target = ReleaseTarget;
        }
        else if (raw.Equals("debug", StringComparison.OrdinalIgnoreCase)
                 || raw.Equals("cdp-debug", StringComparison.OrdinalIgnoreCase))
        {
            target = DebugTarget;
        }
        else
        {
            target = Path.GetFullPath(raw);
        }

        if (mode == "hard" && SamePath(target, selfRoot) && !force)
        {
            return new TargetDecision(
                false,
                target,
                sibling,
                "refuse_hard_self",
                "Hard KillRunning cannot reliably kill this process from inside. " +
                "Default: target=sibling (or switch seats). force=true to override.");
        }

        return new TargetDecision(true, target, sibling, null, null);
    }
}
