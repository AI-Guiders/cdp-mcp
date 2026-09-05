namespace Cdp.Deploy;

public sealed record CdpDeployPlan(
    CdpDeployMode Mode,
    CdpDeployLayout Layout,
    CdpDeploySource Source,
    string Seat,
    string? SelfInstallRoot,
    string? BridgePublishTarget,
    bool KillRunning,
    bool NoNudge,
    bool UseNuGet)
{
    public string ServicePublishRoot =>
        Mode switch
        {
            CdpDeployMode.Soft => Layout.StagedService,
            _ => Layout.ServiceInstall
        };

        public string? BridgePublishRoot =>
        BridgePublishTarget is null
            ? null
            : Mode switch
            {
                CdpDeployMode.Soft => BridgePublishTarget + ".next",
                _ => BridgePublishTarget
            };

        public string? BridgeDebugPublishRoot
    {
        get
        {
            if (BridgePublishTarget is null)
                return null;
            if (CdpDeployPaths.SamePath(BridgePublishTarget, Layout.BridgeDebugInstall))
                return null;
            return Mode switch
            {
                CdpDeployMode.Soft => Layout.StagedBridgeDebug,
                _ => Layout.BridgeDebugInstall
            };
        }
    }
}

public static class CdpDeployPlanner
{
    public static string ClassifySeat(string? installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
            return "other";

        var full = Path.GetFullPath(installRoot);
        var layout = CdpDeployLayout.Default;
        if (CdpDeployPaths.SamePath(full, layout.BridgeReleaseInstall))
            return "cdp";
        if (CdpDeployPaths.SamePath(full, layout.BridgeDebugInstall))
            return "cdp-debug";

        var leaf = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (leaf.Equals("self", StringComparison.OrdinalIgnoreCase)
            && Path.GetDirectoryName(full) is { Length: > 0 } parent)
            return ClassifySeat(parent);

        if (leaf.Equals("cdp-mcp-debug", StringComparison.OrdinalIgnoreCase))
            return "cdp-debug";
        if (leaf.Equals("cdp-mcp", StringComparison.OrdinalIgnoreCase))
            return "cdp";

        return "other";
    }

    public static CdpDeployPlanResult Plan(CdpDeployPlanRequest request)
    {
        var layout = request.Layout ?? CdpDeployLayout.Default;
        try
        {
            layout.ValidateDistinctRoots();
        }
        catch (Exception ex)
        {
            return CdpDeployPlanResult.Fail("layout_invalid", ex.Message);
        }

        var source = request.Source
                     ?? CdpDeploySource.TryResolve(request.RepoSearchRoot)
                     ?? CdpDeploySource.TryResolve(request.SelfInstallRoot);
        if (source is null)
        {
            if (request.Mode == CdpDeployMode.Apply)
                source = CdpDeployInstallPlaceholder.PlaceholderSource;
            else
                return CdpDeployPlanResult.Fail("source_not_found", "Cannot locate cdp-mcp repo (CdpMcp.csproj + CdpMcpBridge).");
        }

        var seat = ClassifySeat(request.SelfInstallRoot);
        return PlanWithSource(layout, source, seat, request);
    }

    public static CdpDeployPlanResult PlanInstallTarget(CdpDeployInstallRequest request)
    {
        var layout = request.Layout ?? CdpDeployLayout.Default;
        try
        {
            layout.ValidateDistinctRoots();
        }
        catch (Exception ex)
        {
            return CdpDeployPlanResult.Fail("layout_invalid", ex.Message);
        }

        var seat = ClassifySeat(request.SelfInstallRoot);
        return PlanWithSource(
            layout,
            source: request.SourcePlaceholder,
            seat,
            new CdpDeployPlanRequest(
                request.Mode,
                request.SelfInstallRoot,
                request.RepoSearchRoot,
                request.TargetRaw,
                request.Force,
                UseNuGet: false,
                NoNudge: false,
                layout,
                request.SourcePlaceholder));
    }

    static CdpDeployPlanResult PlanWithSource(
        CdpDeployLayout layout,
        CdpDeploySource source,
        string seat,
        CdpDeployPlanRequest request)
    {
        return request.Mode switch
        {
            CdpDeployMode.Apply => PlanApply(layout, source, seat, request),
            CdpDeployMode.Hard => PlanHard(layout, source, seat, request),
            CdpDeployMode.Soft => PlanSoft(layout, source, seat, request),
            CdpDeployMode.Rollout => CdpDeployPlanResult.Fail("use_rollout_executor", "Rollout is orchestrated step-by-step."),
            _ => CdpDeployPlanResult.Fail("unknown_mode", $"Unsupported mode {request.Mode}.")
        };
    }

    static CdpDeployPlanResult PlanApply(
        CdpDeployLayout layout,
        CdpDeploySource source,
        string seat,
        CdpDeployPlanRequest request) =>
        CdpDeployPlanResult.Success(new CdpDeployPlan(
            CdpDeployMode.Apply,
            layout,
            source,
            seat,
            request.SelfInstallRoot,
            layout.BridgeReleaseInstall,
            KillRunning: false,
            request.NoNudge,
            request.UseNuGet));

        static CdpDeployPlanResult PlanSoft(
        CdpDeployLayout layout,
        CdpDeploySource source,
        string seat,
        CdpDeployPlanRequest request)
    {
        // ADR-0209: bridge publish is optional — null = service-only deploy.
        var bridgeTarget = ResolveBridgeTarget(layout, seat, request.TargetRaw, request.SelfInstallRoot);

        return CdpDeployPlanResult.Success(new CdpDeployPlan(
            CdpDeployMode.Soft,
            layout,
            source,
            seat,
            request.SelfInstallRoot,
            bridgeTarget,
            KillRunning: false,
            request.NoNudge,
            request.UseNuGet));
    }

        static CdpDeployPlanResult PlanHard(
        CdpDeployLayout layout,
        CdpDeploySource source,
        string seat,
        CdpDeployPlanRequest request)
    {
        // ADR-0209: bridge publish is optional — null = service-only deploy.
        var bridgeTarget = ResolveBridgeTarget(layout, seat, request.TargetRaw, request.SelfInstallRoot);

        if (bridgeTarget is not null
            && !request.Force
            && CdpDeployPaths.SamePath(bridgeTarget, request.SelfInstallRoot))
        {
            return CdpDeployPlanResult.Fail(
                "refuse_hard_self",
                "Hard KillRunning cannot reliably kill this process from inside. Default: target=sibling. force=true to override.");
        }

        return CdpDeployPlanResult.Success(new CdpDeployPlan(
            CdpDeployMode.Hard,
            layout,
            source,
            seat,
            request.SelfInstallRoot,
            bridgeTarget,
            KillRunning: true,
            request.NoNudge,
            request.UseNuGet));
    }

    static string? ResolveBridgeTarget(
        CdpDeployLayout layout,
        string seat,
        string? targetRaw,
        string? selfInstallRoot)
    {
        var raw = (targetRaw ?? "").Trim();
        if (raw.Length == 0
            || raw.Equals("sibling", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("other", StringComparison.OrdinalIgnoreCase))
            return layout.SiblingBridgeForSeat(seat);

        if (raw.Equals("self", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("here", StringComparison.OrdinalIgnoreCase))
                        return selfInstallRoot is not null
                   && CdpDeployPaths.SamePath(selfInstallRoot, layout.ServiceInstall)
                ? null // service-only deploy (ADR-0209) — the bridge is a separate seat
                : selfInstallRoot ?? layout.BridgeReleaseInstall;

        if (raw.Equals("release", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp", StringComparison.OrdinalIgnoreCase))
            return layout.BridgeReleaseInstall;

        if (raw.Equals("debug", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp-debug", StringComparison.OrdinalIgnoreCase))
            return layout.BridgeDebugInstall;

        if (raw.Equals("service", StringComparison.OrdinalIgnoreCase))
            return null;

        var full = Path.GetFullPath(raw);
        if (CdpDeployPaths.SamePath(full, layout.ServiceInstall))
            return null;

        return full;
    }
}

public sealed record CdpDeployPlanRequest(
    CdpDeployMode Mode,
    string? SelfInstallRoot,
    string? RepoSearchRoot,
    string? TargetRaw,
    bool Force,
    bool UseNuGet,
    bool NoNudge,
    CdpDeployLayout? Layout = null,
    CdpDeploySource? Source = null);

public sealed record CdpDeployInstallRequest(
    CdpDeployMode Mode,
    string? SelfInstallRoot,
    string? RepoSearchRoot,
    string? TargetRaw,
    bool Force,
    CdpDeployLayout? Layout = null,
    CdpDeploySource? SourcePlaceholder = null)
{
        public static CdpDeployInstallRequest ForResolve(
        CdpDeployMode mode,
        string? selfInstallRoot,
        string? targetRaw,
        bool force) =>
        new(mode, selfInstallRoot, null, targetRaw, force, CdpDeployLayout.Default, CdpDeployInstallPlaceholder.PlaceholderSource);
}

internal static class CdpDeployInstallPlaceholder
{
    internal static readonly CdpDeploySource PlaceholderSource = new(
        RepoRoot: ".",
        ServiceProject: "CdpMcp.csproj",
        BridgeProject: "CdpMcpBridge/CdpMcpBridge.csproj",
        ConfigTemplate: "config/cdp-mcp.toml",
        PreserveConfigToml: null);
}

public sealed record CdpDeployPlanResult(bool Ok, CdpDeployPlan? Plan, string? Error, string? Hint)
{
    public static CdpDeployPlanResult Success(CdpDeployPlan plan) => new(true, plan, null, null);

    public static CdpDeployPlanResult Fail(string error, string hint) => new(false, null, error, hint);
}
