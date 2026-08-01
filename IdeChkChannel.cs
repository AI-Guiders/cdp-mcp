#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=ecl</c> (alias <c>chk</c>) — Electronic Checklist / ECL (Boeing-style),
/// Memory Items + AUTO/DO/CONFIRM; catalog customize (add/remove/link).
/// Overlay: <c>ecl.overlay</c> (fallback <c>chk.overlay</c>); acks: <c>ecl.acks</c>.
/// </summary>
internal static partial class IdeChkChannel
{
    public const string SchemaVersion = "ecl_organ/v1";
    public const string OverlayKey = "ecl.overlay";
    public const string AcksKey = "ecl.acks";
    const string LegacyOverlayKey = "chk.overlay";
    const string LegacyAcksKey = "chk.acks";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public sealed record ProbeCtx(
        bool ProjectOpen,
        bool TaskOpen,
        bool IgniteIdle,
        bool GitKnown,
        bool GitDirty,
        bool TestsGreen,
        bool TestsFailed,
        bool ProblemsClean,
        bool DapStopped,
        bool DapActive,
        bool SniperOk,
        string Phase,
        string? Intent);

    public sealed record ItemDef(
        string Id,
        string Kind,
        string Text,
        string? Probe = null,
        string? Action = null,
        bool Required = true);

    public sealed record ChecklistDef(
        string Id,
        string Title,
        IReadOnlyList<string> Links,
        IReadOnlyList<ItemDef> MemoryItems,
        IReadOnlyList<ItemDef> Items,
        bool Builtin = true,
        bool Enabled = true);

    public sealed record ItemSnap(
        string Id,
        string Kind,
        string Text,
        bool Done,
        bool Required,
        string? Probe,
        string? Action,
        bool Acked);

    public sealed record RunSnap(
        string Id,
        string Title,
        IReadOnlyList<string> Links,
        bool Builtin,
        bool Enabled,
        bool Active,
        int Done,
        int Total,
        int OpenRequired,
        IReadOnlyList<ItemSnap> MemoryItems,
        IReadOnlyList<ItemSnap> Items);

    public sealed record Snap(
        bool Ok,
        string Pulse,
        int ActiveCount,
        int OpenRequired,
        string? HotId,
        IReadOnlyList<RunSnap> Active,
        IReadOnlyList<ChecklistDef> Catalog);

    public static Snap Build(ProbeCtx ctx, bool catalogOnly = false)
    {
        var catalog = EffectiveCatalog();
        var acks = LoadAcks();
        var runs = new List<RunSnap>();
        foreach (var def in catalog.Where(c => c.Enabled))
        {
            var active = catalogOnly || MatchesAny(def.Links, ctx);
            if (!catalogOnly && !active)
                continue;
            runs.Add(Evaluate(def, ctx, acks, active));
        }

        if (catalogOnly)
        {
            var enabled = catalog.Count(c => c.Enabled);
            return new Snap(
                true,
                $"ecl · catalog {enabled}/{catalog.Count}",
                0,
                0,
                null,
                runs,
                catalog);
        }

        var activeRuns = runs.Where(r => r.Active).ToList();
        var openReq = activeRuns.Sum(r => r.OpenRequired);
        var hot = activeRuns
            .OrderByDescending(r => r.OpenRequired)
            .ThenBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        var pulse = activeRuns.Count == 0
            ? "ecl · idle"
            : openReq == 0
                ? $"ecl · {activeRuns.Count} clear"
                : hot is null
                    ? $"ecl · open×{openReq}"
                    : $"ecl · {hot.Id} {hot.Done}/{hot.Total} (open×{openReq})";

        return new Snap(true, pulse, activeRuns.Count, openReq, hot?.Id, activeRuns, catalog);
    }

    public static object Handle(
        ProbeCtx ctx,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        var merged = FlattenArgs(args);
        var op = (Opt(merged, "op") ?? Opt(merged, "pulse") ?? "run").Trim().ToLowerInvariant();

        object? action = null;
        switch (op)
        {
            case "list" or "catalog":
                return Board(Build(ctx, catalogOnly: true), action, "catalog");
            case "add":
                action = DoAdd(merged);
                break;
            case "remove" or "rm" or "delete":
                action = DoRemove(merged);
                break;
            case "link":
                action = DoLink(merged, add: true);
                break;
            case "unlink":
                action = DoLink(merged, add: false);
                break;
            case "enable" or "on":
                action = DoEnable(merged, enable: true);
                break;
            case "disable" or "off":
                action = DoEnable(merged, enable: false);
                break;
            case "ack" or "done" or "check":
                action = DoAck(merged);
                break;
            case "unack" or "undo":
                action = DoAck(merged, unack: true);
                break;
            case "reset":
                action = DoReset(merged);
                break;
            case "run" or "active" or "scene":
                break;
            default:
                return new
                {
                    ok = false,
                    schema = SchemaVersion,
                    go = "ecl",
                    error = "unknown_op",
                    hint = "op=run|list|add|remove|link|unlink|enable|disable|ack|reset"
                };
        }

        var snap = Build(ctx);
        return Board(snap, action, "run");
    }

    public static ProbeCtx CtxFrom(
        SessionContext session,
        bool taskOpen,
        bool igniteIdle,
        bool gitKnown,
        bool gitDirty,
        bool testsGreen,
        bool testsFailed,
        bool problemsClean,
        bool dapStopped,
        bool dapActive,
        bool sniperOk)
    {
        var intent = session.Intent is { } i ? CdpEnumParse.ToWire(i) : null;
        return new ProbeCtx(
            !string.IsNullOrWhiteSpace(session.ProjectRoot),
            taskOpen,
            igniteIdle,
            gitKnown,
            gitDirty,
            testsGreen,
            testsFailed,
            problemsClean,
            dapStopped,
            dapActive,
            sniperOk,
            CdpEnumParse.ToWire(session.Phase),
            intent);
    }

    static object Board(Snap snap, object? action, string mode) => new
    {
        ok = snap.Ok,
        go = "ecl",
        schema = SchemaVersion,
        mode,
        pulse = snap.Pulse,
        title = "ECL",
        note = "Electronic Checklist (ECL) — Memory first, AUTO probes, DO/CONFIRM via ack, ALLOW = standing. Alias: go=chk.",
        active_count = snap.ActiveCount,
        open_required = snap.OpenRequired,
        hot = snap.HotId,
        runs = snap.Active.Select(RunCard).ToArray(),
        catalog = snap.Catalog.Select(c => new
        {
            id = c.Id,
            title = c.Title,
            links = c.Links,
            builtin = c.Builtin,
            enabled = c.Enabled,
            memory = c.MemoryItems.Count,
            items = c.Items.Count
        }).ToArray(),
        action,
        hint = "CCL: ecl | ecl list | ecl link ship phase:verify | ecl ack ship push | ecl add id=… link=phase:act (alias chk)"
    };

    static object RunCard(RunSnap r) => new
    {
        id = r.Id,
        title = r.Title,
        links = r.Links,
        builtin = r.Builtin,
        enabled = r.Enabled,
        active = r.Active,
        done = r.Done,
        total = r.Total,
        open_required = r.OpenRequired,
        memory_items = r.MemoryItems.Select(ItemCard).ToArray(),
        items = r.Items.Select(ItemCard).ToArray()
    };

    static object ItemCard(ItemSnap i) => new
    {
        id = i.Id,
        kind = i.Kind,
        text = i.Text,
        done = i.Done,
        required = i.Required,
        probe = i.Probe,
        action = i.Action,
        acked = i.Acked
    };
}
