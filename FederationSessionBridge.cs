#if CDP_FEDERATION_IDE_SESSION
using System.Text.Json;
using AIGuiders.Platform.Execution.Ide.Session;
using AIGuiders.Platform.Modeling.Ide.Session;
using Cdp.Core;

namespace CdpMcp;

/// <summary>CDP dogfood host for federation <c>Modeling.Ide.Session</c> graph SSOT (ADR-0005).</summary>
internal static class FederationSessionBridge
{
    public static bool IsAvailable => true;

    public static FederationGraphPulseDto? TryPulse(SessionContext session)
    {
        var anchor = session.SolutionOrProjectPath;
        if (anchor is not { Length: > 0 })
            return null;

        try
        {
            var opened = FederationSessionRuntime.Open(anchor);
            var graph = opened.Runtime.Session.Graph;

            return new FederationGraphPulseDto
            {
                Available = true,
                AnchorPath = graph.AnchorPath,
                Phase = opened.Runtime.Session.Phase.ToString(),
                ProjectCount = graph.Projects.Count,
                ProjectEdgeCount = graph.ProjectEdges.Count,
                FileOwnershipCount = graph.FileOwnership.Count,
                LedgerRevision = opened.Runtime.Ledger.NextRevision - 1,
                GraphValid = opened.IsValid,
                IssueCount = opened.Validation.Issues.Count
            };
        }
        catch (Exception ex)
        {
            return new FederationGraphPulseDto
            {
                Available = false,
                AnchorPath = anchor,
                Error = ex.Message
            };
        }
    }

    public static string BuildSceneJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> callArgs,
        JsonSerializerOptions pretty)
    {
        var anchor = OptPath(callArgs) ?? session.SolutionOrProjectPath;
        if (anchor is not { Length: > 0 })
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                reason = "no_anchor",
                hint = "cdp_open a .slnx/.sln/.csproj first or pass path="
            }, pretty);
        }

        try
        {
            var opened = FederationSessionRuntime.Open(anchor);
            var graph = opened.Runtime.Session.Graph;

            return JsonSerializer.Serialize(new
            {
                ok = true,
                schema = "federation.ide.session.scene/v1",
                anchor_path = graph.AnchorPath,
                phase = opened.Runtime.Session.Phase.ToString(),
                graph_valid = opened.IsValid,
                validation_issues = opened.Validation.Issues.Select(i => i.Message).ToArray(),
                ledger_revision = opened.Runtime.Ledger.NextRevision - 1,
                materialized_count = opened.Runtime.Materialized.Entries.Count,
                projects = graph.Projects.Select(p => new
                {
                    id = ProjectId.value(p.Id),
                    path = p.AbsolutePath,
                    kind = p.Kind.ToString(),
                    capability_count = p.Capabilities.Count
                }).ToArray(),
                project_edges = graph.ProjectEdges.Select(e => new
                {
                    from = ProjectId.value(e.From),
                    to = ProjectId.value(e.To)
                }).ToArray(),
                file_ownership_count = graph.FileOwnership.Count,
                next = new object[]
                {
                    new { go = "ide_session_scene", label = "Refresh graph", why = "detail=full" },
                    new { go = "build", label = "Build", why = "on-demand job lane @ freeze (Phase 2+)" }
                }
            }, pretty);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { ok = false, anchor_path = anchor, error = ex.Message }, pretty);
        }
    }

    static string? OptPath(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (args.TryGetValue("path", out var p) && p.GetString() is { Length: > 0 } s)
            return s;
        if (args.TryGetValue("anchor_path", out var a) && a.GetString() is { Length: > 0 } ap)
            return ap;
        return null;
    }
}
#else
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

internal static class FederationSessionBridge
{
    public static bool IsAvailable => false;

    public static FederationGraphPulseDto? TryPulse(SessionContext _) => null;

    public static string BuildSceneJson(
        SessionContext _,
        IReadOnlyDictionary<string, JsonElement> __,
        JsonSerializerOptions pretty) =>
        JsonSerializer.Serialize(new
        {
            ok = false,
            reason = "federation_ide_session_unavailable",
            hint = "Build CDP with UseLocalGuidersPlatform + Execution.Ide.Session sibling."
        }, pretty);
}
#endif
