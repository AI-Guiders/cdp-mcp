#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Cockpit.Surface;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=inventory</c> / Meta <c>cdp_inventory</c> — dense throughput gap list [A], not W-spray.
/// Live SoftInstrument Meta host dig (RouteOne go|tool ≠ place-only) so Autoi does not re-wire shipped Crm/Arch.
/// </summary>
internal static class IdeInventoryChannel
{
    public const string SchemaVersion = "inventory_channel/v1";
    public const string ToolName = "cdp_inventory";
    public const string GoName = "inventory";

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(session, args), new JsonSerializerOptions { WriteIndented = true });

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "pulse" or "a" => Pulse(session),
            _ => Scene(session)
        };
    }

    public static string PulseLine(SessionContext? session = null)
    {
        var gaps = BuildGaps(session).Count;
        var wave = IdeWaveChannel.PulseLine();
        var host = SoftInstrumentHostPulse();
        return $"inventory · gaps×{gaps} · {host} · {wave}";
    }

    static object Scene(SessionContext session)
    {
        var gaps = BuildGaps(session);
        var wave = IdeWaveChannel.TryLoadActive();
        var host = ProbeSoftInstrumentHosts();
        var recommend = Math.Clamp(Math.Max(8, gaps.Count(g => g.Status is "gap" or "active")), 8, 15);
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "scene",
            go = GoName,
            tool = ToolName,
            pulse = PulseLine(session),
            batch_size_recommend = recommend,
            softinstrument_host = new
            {
                covered = host.Covered,
                missing = host.Missing.Count,
                total = host.Total,
                gaps = host.Missing.Take(12).ToArray(),
                status = host.Missing.Count == 0 ? "CLOSED" : "gap"
            },
            wave = wave is null
                ? (object)new { active = false, pulse = "wave · idle" }
                : new
                {
                    active = true,
                    id = wave.Id,
                    title = wave.Title,
                    status = wave.Status,
                    done = wave.Items.Count(i => i.Status == "done"),
                    total = wave.Items.Count,
                    pulse = IdeWaveChannel.PulseLine()
                },
            gaps = gaps.Select(g => new { id = g.Id, status = g.Status, note = g.Note }).ToArray(),
            ops = new[] { "scene", "pulse" },
            next = new object[]
            {
                new { go = "plan", label = "Seed / fly wave", why = "cmd=wave seed title=… items=a;b;c" },
                new { go = "verify_wave", label = "Ship checklist", why = "op=scene" },
                new { go = "pressure_desk", label = "Stash wave[]", why = "op=stash wave=…" }
            },
            hint =
                "Throughput inventory [A]: list gaps → batch (~8–15) → ship one wave. Soft FileLines CLOSED — do not mill. " +
                "softinstrument_host = live Meta host dig — do not re-wire CLOSED. Not W-spray; not biped serial peel."
        };
    }

    static object Pulse(SessionContext session) => new
    {
        schema = SchemaVersion,
        ok = true,
        op = "pulse",
        go = GoName,
        pulse = PulseLine(session)
    };

    static List<(string Id, string Status, string Note)> BuildGaps(SessionContext? session)
    {
        var host = ProbeSoftInstrumentHosts();
        var gaps = new List<(string, string, string)>
        {
            ("soft-filelines", "CLOSED", "Do not reopen peel mill — Soft FileLines CLOSED."),
            ("citizen-sse", "shipped", "Citizen SSE 0.5.644 — stream+budgets; do not re-litigate."),
            ("meta-host-softinstruments",
                host.Missing.Count == 0 ? "CLOSED" : "gap",
                host.Missing.Count == 0
                    ? $"SoftInstrument Meta hosts covered ({host.Covered}/{host.Total} excl Plan) — do not re-wire Crm/Arch."
                    : $"SoftInstrument Meta host gaps ×{host.Missing.Count}: {string.Join(", ", host.Missing.Take(6))} — wire @intent host, not FileLines."),
            ("throughput-wave", IdeWaveChannel.HasActiveOpen() ? "active" : "gap",
                IdeWaveChannel.HasActiveOpen()
                    ? "Active wave — fly items to shipped."
                    : "No active wave — cmd=wave seed title=… items=a;b;c; go=plan."),
            ("pressure-wave-field", "afford", "Pressure stash accepts wave= JSON / ## wave — recall returns wave."),
            ("sa-biped", "afford", "SA biped_mill warns when act + no wave — next go=inventory|wave."),
            ("verify-wave", "afford", "go=verify_wave checklist before dual hard (not in-proc KillRunning)."),
            ("domain-stamp", "habit", "Stamp .cdp/domain after ship; Voice Letter only after live dogfood."),
            ("list-batch-ship", "canon", "list → batch → ship — Autoi timer ≠ single-item mill license.")
        };

        _ = session;
        return gaps;
    }

    static string SoftInstrumentHostPulse()
    {
        var host = ProbeSoftInstrumentHosts();
        return host.Missing.Count == 0
            ? $"meta-host CLOSED {host.Covered}/{host.Total}"
            : $"meta-host gap×{host.Missing.Count}";
    }

    internal static SoftInstrumentHostSnap ProbeSoftInstrumentHosts()
    {
        var cat = new SoftInstrumentBoardMetaCatalog();
        var missing = new List<string>();
        var covered = 0;
        var total = 0;
        foreach (SoftInstrumentKind kind in Enum.GetValues<SoftInstrumentKind>())
        {
            if (kind == SoftInstrumentKind.Plan)
                continue; // TM place organ — not Meta host mill

            total++;
            var meta = cat.Require(kind);
            if (HostProbes(meta.Go, meta.Tool).Any(IsHostExecuteIntent))
            {
                covered++;
                continue;
            }

            missing.Add($"{kind}:{meta.Go}");
        }

        return new SoftInstrumentHostSnap(covered, total, missing);
    }

    static IEnumerable<string> HostProbes(string go, string tool)
    {
        foreach (var s in new[] { go, tool })
        {
            if (string.IsNullOrWhiteSpace(s))
                continue;
            yield return s;
            if (s.EndsWith("_desk", StringComparison.OrdinalIgnoreCase) && s.Length > 5)
                yield return s[..^5];
            if (s.StartsWith("cdp_", StringComparison.OrdinalIgnoreCase) && s.Length > 4)
                yield return s[4..];
        }
    }

    /// <summary>
    /// Host-execute Verb even when Ok=false (e.g. find without query) — still means Meta host exists.
    /// Place-only Go/Drill/Detail/PaneFull and Refuse/Unknown ≠ covered.
    /// </summary>
    static bool IsHostExecuteIntent(string intent)
    {
        if (string.IsNullOrWhiteSpace(intent))
            return false;
        var route = CitizenIntentRouter.RouteOne(intent);
        return route.Verb is not CitizenIntentRouter.Verb.Go
            and not CitizenIntentRouter.Verb.Drill
            and not CitizenIntentRouter.Verb.Detail
            and not CitizenIntentRouter.Verb.PaneFull
            and not CitizenIntentRouter.Verb.Refuse
            and not CitizenIntentRouter.Verb.Unknown;
    }

    internal sealed record SoftInstrumentHostSnap(int Covered, int Total, IReadOnlyList<string> Missing);

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            _ => el.ToString()
        };
    }
}
