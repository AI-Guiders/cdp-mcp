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

}
