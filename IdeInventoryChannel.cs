#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=inventory</c> / Meta <c>cdp_inventory</c> — dense throughput gap list [A], not W-spray.
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
        return $"inventory · gaps×{gaps} · {wave}";
    }

    static object Scene(SessionContext session)
    {
        var gaps = BuildGaps(session);
        var wave = IdeWaveChannel.TryLoadActive();
        var recommend = Math.Clamp(gaps.Count, 8, 15);
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "scene",
            go = GoName,
            tool = ToolName,
            pulse = PulseLine(session),
            batch_size_recommend = recommend,
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
                new { go = "plan", label = "Seed / fly wave", why = "cmd=wave seed …" },
                new { go = "verify_wave", label = "Ship checklist", why = "op=scene" },
                new { go = "pressure_desk", label = "Stash wave[]", why = "op=stash wave=…" }
            },
            hint =
                "Throughput inventory [A]: list gaps → batch (~8–15) → ship one wave. Soft FileLines CLOSED — do not mill. " +
                "Not W-spray; not biped serial peel."
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
        var gaps = new List<(string, string, string)>
        {
            ("soft-filelines", "CLOSED", "Do not reopen peel mill — Soft FileLines CLOSED."),
            ("citizen-sse", "shipped", "Citizen SSE 0.5.644 — stream+budgets; do not re-litigate."),
            ("throughput-wave", IdeWaveChannel.HasActiveOpen() ? "active" : "gap",
                IdeWaveChannel.HasActiveOpen()
                    ? "Active wave — fly items to shipped."
                    : "No active wave — cmd=wave seed labels; go=plan."),
            ("pressure-wave-field", "afford", "Pressure stash accepts wave= JSON / ## wave — recall returns wave."),
            ("sa-biped", "afford", "SA biped_mill warns when act + no wave — next go=inventory|wave."),
            ("verify-wave", "afford", "go=verify_wave checklist before dual hard (not in-proc KillRunning)."),
            ("domain-stamp", "habit", "Stamp .cdp/domain after ship; Voice Letter only after live dogfood."),
            ("list-batch-ship", "canon", "list → batch → ship — Autoi timer ≠ single-item mill license.")
        };

        _ = session; // phase available for future scoring
        return gaps;
    }

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
