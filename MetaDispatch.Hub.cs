#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using Cdp.Core;
using Cdp.Evidence;
using Cdp.ScriptableIde;
using CdpMcp.Backends;
using ModelContextProtocol.Protocol;

namespace CdpMcp;

internal static partial class MetaDispatch
{
    static async Task<string?> HubAsync(
        MetaDispatchDeps d,
        string name,
        IReadOnlyDictionary<string, JsonElement> callArgs,
        CancellationToken cancellationToken,
        object? warm = null)
    {
        var session = d.Session;
        var docStore = d.DocStore;
        var byDomain = d.ByDomain;
        var modules = d.Modules;
        var allAffordances = d.AllAffordances;
        var settings = d.Settings;
        var Pretty = d.Pretty;
        var shellHabitat = d.ShellHabitat;
        var mcpOutlet = d.McpOutlet;
        var internetBrowser = d.InternetBrowser;
        var ideSettings = d.IdeSettings;
        var workspaceStore = d.WorkspaceStore;
        var workspaceState = d.WorkspaceState;
        var workspaceDbPath = d.WorkspaceDbPath;
        var NotifyListChanged = d.NotifyListChanged;
        var EnsureOpenRecentWired = d.EnsureOpenRecentWired;
        var EnsureWorkspaceDb = d.EnsureWorkspaceDb;
        var DispatchAsync = d.DispatchToolAsync;
        var DispatchCdpWork = d.DispatchCdpWork;

        switch (name)
        {
    case "cdp_tools":
    {
        var qPhase = session.Phase;
        var qObj = session.Object;
        CdpIntent? qIntent = session.Intent;
        string? qLang = session.Language;
        if (callArgs.TryGetValue("phase", out var p2) && CdpEnumParse.TryParsePhase(p2.GetString(), out var pp))
            qPhase = pp;
        if (callArgs.TryGetValue("object", out var o2) && CdpEnumParse.TryParseObject(o2.GetString(), out var oo))
            qObj = oo;
        if (callArgs.TryGetValue("intent", out var i2) && CdpEnumParse.TryParseIntent(i2.GetString(), out var ii))
            qIntent = ii;
        if (callArgs.TryGetValue("language", out var l2) && settings.Languages.TryNormalize(l2.GetString(), out var ll))
            qLang = CdpLanguages.IsAny(ll) ? null : ll;
        var limit = PhaseObjectCatalog.DefaultListToolsLimit;
        if (callArgs.TryGetValue("limit", out var lim) && lim.TryGetInt32(out var li))
            limit = li;
        var hits = PhaseObjectCatalog.Query(allAffordances, qPhase, qObj, qIntent, limit, qLang);
        return JsonSerializer.Serialize(new
        {
            phase = CdpEnumParse.ToWire(qPhase),
            @object = CdpEnumParse.ToWire(qObj),
            intent = qIntent is null ? null : CdpEnumParse.ToWire(qIntent.Value),
            language = qLang,
            total = hits.Count,
            tools = hits.Select(h => new
            {
                name = h.Affordance.PrefixedName,
                score = h.Score,
                cost = h.Affordance.Cost,
                risk = h.Affordance.Risk,
                hint = h.Affordance.Hint
            })
        }, Pretty);
    }
    case "cdp_cockpit":
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWorkspaceDb(); // desk_seats + script_last_run (WitDB)

        return await IdeCockpit.BuildAsync(
                session,
                docStore,
                shellHabitat,
                internetBrowser,
                ideSettings,
                mcpOutlet,
                byDomain,
                workspaceStore,
                workspaceState,
                callArgs,
                DispatchAsync,
                cancellationToken,
                warm)
            .ConfigureAwait(false);
    }
    case "cdp_session":
    {
        cancellationToken.ThrowIfCancellationRequested();
        var shortlistLimit = 12;
        if (callArgs.TryGetValue("shortlist_limit", out var sl) && sl.TryGetInt32(out var sli))
            shortlistLimit = sli;
        var (wid, sid, sname, dbPath) = (null as string, null as string, null as string, workspaceDbPath);
        if (workspaceStore is not null)
            (wid, sid, sname, dbPath) = workspaceStore.PlaneIds(workspaceState);
        var workspacePlane = new WorkspacePlaneDto
        {
            ActiveIntentId = wid,
            ActiveSceneId = sid,
            ActiveSceneName = sname,
            DatabasePath = dbPath
        };
        var plane = await SessionPlane.BuildSessionAsync(
            session, modules, byDomain, allAffordances, callArgs, shortlistLimit, workspacePlane).ConfigureAwait(false);
        return JsonSerializer.Serialize(plane, Pretty);
    }
    case "cdp_work":
    {
        // Escape hatch: Cursor host may omit standalone cdp_buffer / cdp_debug from ListTools;
        // buffer_* and debug_* ops ride on already-advertised cdp_work.
        string? workOp = null;
        if (callArgs.TryGetValue("op", out var workOpEl))
        {
            workOp = workOpEl.ValueKind == JsonValueKind.String
                ? workOpEl.GetString()
                : workOpEl.ToString();
        }

        if (workOp is { Length: > 0 }
            && workOp.Trim().StartsWith("buffer_", StringComparison.OrdinalIgnoreCase))
        {
            var sub = workOp.Trim()["buffer_".Length..].Trim().ToLowerInvariant();
            var mapped = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var kv in callArgs)
                mapped[kv.Key] = kv.Value;
            mapped["op"] = JsonSerializer.SerializeToElement(sub);
            return await DocumentEditPlane
                .DispatchAsync("cdp_buffer", docStore, session, byDomain, mapped, cancellationToken)
                .ConfigureAwait(false);
        }

        if (workOp is { Length: > 0 }
            && workOp.Trim().StartsWith("debug_", StringComparison.OrdinalIgnoreCase))
        {
            var sub = workOp.Trim()["debug_".Length..].Trim().ToLowerInvariant();
            var mapped = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var kv in callArgs)
                mapped[kv.Key] = kv.Value;
            mapped["op"] = JsonSerializer.SerializeToElement(sub);
            return await DebugPlane
                .DispatchAsync(session, byDomain, mapped, cancellationToken)
                .ConfigureAwait(false);
        }

        return JsonSerializer.Serialize(DispatchCdpWork(callArgs), Pretty);
    }
    default:
        return null;
        }
    }
}
