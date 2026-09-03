#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.Deploy;

namespace CdpMcp;

/// <summary>Dual-instance deploy — C# SSOT (<see cref="CdpDeployOrchestrator"/>), ADR-0198.</summary>
internal static partial class IdeDeploy
{
    public const string Schema = "deploy/v0";
    public const string ReleaseTarget = @"D:\cdp-mcp";
    public const string DebugTarget = @"D:\cdp-mcp-debug";
    public const string ServiceTarget = @"D:\cdp-service";
    public const string ScriptName = "publish-and-deploy.ps1";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly object PublishGate = new();

    public static string Run(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var mode = NormalizeMode(Opt(args, "mode") ?? "hard");
        var dryRun = IsTruthy(args, "dry_run") || IsTruthy(args, "peek");
        var force = IsTruthy(args, "force");
        var useNuGet = IsTruthy(args, "use_nuget") || IsTruthy(args, "UseNuGet");
        var noNudge = IsTruthy(args, "no_nudge") || IsTruthy(args, "NoNudgeMcp");

        if (mode == "rollout")
            return Rollout(session, args);

        var selfRoot = ResolveSelfInstallRoot();
        var seat = ClassifySeat(selfRoot);
        var resolved = ResolveTarget(selfRoot, seat, Opt(args, "target") ?? Opt(args, "to"), mode, force);
        if (!resolved.Ok)
            return Fail(mode, selfRoot, seat, resolved.Target, resolved.Error!, resolved.Hint);

        if (dryRun)
            return DryRunPayload(mode, selfRoot, seat, resolved, BuildPlan(session, mode, selfRoot, resolved, useNuGet, noNudge, force));

        if (!Monitor.TryEnter(PublishGate))
        {
            return Fail(mode, selfRoot, seat, resolved.Target, "deploy_in_flight",
                "Another cdp_deploy is still publishing — wait, then retry (soft/hard sequential).");
        }

        try
        {
            var planResult = BuildPlan(session, mode, selfRoot, resolved, useNuGet, noNudge, force);
            if (!planResult.Ok)
                return Fail(mode, selfRoot, seat, resolved.Target, planResult.Error!, planResult.Hint);

            var started = DateTime.UtcNow;
            CdpDeployStepResult step;
            try
            {
                step = CdpDeployOrchestrator.Run(planResult.Plan!);
            }
            catch (Exception ex)
            {
                return ExecuteFailure(mode, selfRoot, seat, resolved, ex.Message, started);
            }

            return ExecuteSuccess(mode, selfRoot, seat, resolved, args, step, started);
        }
        finally
        {
            Monitor.Exit(PublishGate);
        }
    }

    static CdpDeployPlanResult BuildPlan(
        SessionContext session,
        string mode,
        string? selfRoot,
        TargetDecision resolved,
        bool useNuGet,
        bool noNudge,
        bool force)
    {
        var searchRoot = session.ProjectRoot;
        if (string.IsNullOrWhiteSpace(searchRoot) && session.SolutionOrProjectPath is { Length: > 0 } sp)
            searchRoot = Path.GetDirectoryName(sp);

        return CdpDeployPlanner.Plan(new CdpDeployPlanRequest(
            CdpDeployModeParser.Parse(mode),
            selfRoot,
            searchRoot,
            resolved.TargetRaw,
            force,
            useNuGet,
            noNudge,
            Layout: CdpDeployLayout.Default,
            Source: CdpDeploySource.TryResolve(searchRoot)));
    }

    public static string ClassifySeat(string? installRoot) =>
        CdpDeployPlanner.ClassifySeat(installRoot);
}
