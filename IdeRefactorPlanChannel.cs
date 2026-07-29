#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=refactor_plan</c> / Meta <c>cdp_refactor</c> — decide before cut.
/// Axes: debt · budget · blast · partials · <b>recommend</b> (one package next cut).
/// After SA verdict; does not replace go=sa_desk.
/// </summary>
internal static partial class IdeRefactorPlanChannel
{
    public const string Schema = "refactor_plan/v0.1";
    public const string ToolName = "cdp_refactor";
    public const string GoName = "refactor_plan";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string HandleJson(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args) =>
        JsonSerializer.Serialize(Handle(store, session, args), Pretty);

    public static object Handle(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "plan").Trim().ToLowerInvariant();
        var result = op switch
        {
            "debt" or "map" or "hotspots" => Debt(store, session, args),
            "budget" or "what_if" => Budget(store, session, args),
            "blast" or "radius" => Blast(args),
            "partials" or "seam" => Partials(session, args),
            "recommend" or "next_cut" or "cut" => Recommend(store, session, args),
            "pulse" => Pulse(store, session, args),
            _ => Plan(store, session, args)
        };
        PublishGlass(store, session);
        return result;
    }

    /// <summary>Desk/chrome pulse from top debt hotspots.</summary>
    public static string PulseLine(DocumentBufferStore store, SessionContext session)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var debt = BuildDebt(store, session, args, max: 3);
        return debt.Count == 0
            ? "refactor · idle · go=refactor"
            : $"refactor · hotspots={debt.Count} · {debt.PulseTail} · go=refactor";
    }

    /// <summary>Mirror refactor debt pulse to flat CIDE chrome latch (not EICAS).</summary>
    public static void PublishGlass(DocumentBufferStore store, SessionContext session)
    {
        try
        {
            var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            var debt = BuildDebt(store, session, args, max: 3);
            var pulse = debt.Count == 0
                ? "refactor · idle · go=refactor"
                : $"refactor · hotspots={debt.Count} · {debt.PulseTail} · go=refactor";
            // Dark Cockpit: chrome only while size/debt hotspots remain.
            CideRefactorLatch.Publish(active: debt.Count > 0, pulse, debt.Count);
        }
        catch
        {
            /* best-effort */
        }
    }

    static object Pulse(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var debt = BuildDebt(store, session, args, max: 3);
        return new
        {
            ok = true,
            schema = Schema,
            go = GoName,
            tool = ToolName,
            op = "pulse",
            pulse = $"refactor_plan · hotspots={debt.Count} · {debt.PulseTail}",
            hint = "op=plan for full decide card"
        };
    }

    static object Plan(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var debt = BuildDebt(store, session, args, max: 8);
        var budget = BuildBudget(store, session, args, debt);
        var blast = BuildBlast(args);
        var partials = BuildPartials(session, args);
        var recommend = BuildRecommend(store, session, args, debt, budget, partials);
        var top = debt.Items.FirstOrDefault();
        var pulse = TryRecommendPulse(recommend)
            ?? (top is null
                ? "refactor_plan · no hotspots in scope"
                : $"refactor_plan · top {Rel(session.ProjectRoot, top.Path)} · {top.Metric}={top.Value}");

        return new
        {
            ok = true,
            schema = Schema,
            go = GoName,
            tool = ToolName,
            op = "plan",
            pulse,
            recommend,
            debt = debt.Card(),
            budget,
            blast,
            partials,
            next = PreferRecommendNext(recommend, BuildNext(top, blast)),
            hint = "Act on recommend.cut — detail axes below only if needed. sa_desk still for dirty/clones."
        };
    }

    static object Recommend(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var debt = BuildDebt(store, session, args, max: 8);
        var budget = BuildBudget(store, session, args, debt);
        var partials = BuildPartials(session, args);
        var recommend = BuildRecommend(store, session, args, debt, budget, partials);
        return new
        {
            ok = true,
            schema = Schema,
            go = GoName,
            tool = ToolName,
            op = "recommend",
            pulse = TryRecommendPulse(recommend) ?? "refactor_plan · recommend",
            recommend,
            hint = "Slim card — same recommend as op=plan without full debt dump."
        };
    }

    static string? TryRecommendPulse(object recommend)
    {
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(recommend));
            return doc.RootElement.TryGetProperty("pulse", out var p) ? p.GetString() : null;
        }
        catch { return null; }
    }

    static object[] PreferRecommendNext(object recommend, object[] fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(recommend));
            if (doc.RootElement.TryGetProperty("next", out var narr) && narr.ValueKind == JsonValueKind.Array
                && narr.GetArrayLength() > 0)
            {
                return narr.EnumerateArray()
                    .Select(n => (object)new
                    {
                        go = n.TryGetProperty("go", out var g) ? g.GetString() : null,
                        label = n.TryGetProperty("label", out var lab) ? lab.GetString() : null,
                        why = n.TryGetProperty("why", out var w) ? w.GetString() : null
                    })
                    .ToArray();
            }
        }
        catch { /* fall through */ }

        return fallback;
    }

    static object Debt(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var debt = BuildDebt(store, session, args, max: 12);
        return new
        {
            ok = true,
            schema = Schema,
            go = GoName,
            tool = ToolName,
            op = "debt",
            pulse = debt.PulseLine,
            debt = debt.Card(),
            hint = "Ranked size debt. path= for single file; else open buffers + project *.cs by file_lines."
        };
    }

    static object Budget(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var debt = BuildDebt(store, session, args, max: 3);
        var budget = BuildBudget(store, session, args, debt);
        return new
        {
            ok = true,
            schema = Schema,
            go = GoName,
            tool = ToolName,
            op = "budget",
            pulse = BudgetPulse(budget),
            budget,
            hint = "Pass after_lines= / after_method_lines= / extract_lines= for what-if vs warn/fail."
        };
    }

    static object Blast(IReadOnlyDictionary<string, JsonElement> args)
    {
        var blast = BuildBlast(args);
        return new
        {
            ok = true,
            schema = Schema,
            go = GoName,
            tool = ToolName,
            op = "blast",
            pulse = BlastPulse(blast),
            blast,
            hint = "Live usages via bare find_usages — this card is decide next[], not a sync Roslyn dump."
        };
    }

    static object Partials(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var partials = BuildPartials(session, args);
        return new
        {
            ok = true,
            schema = Schema,
            go = GoName,
            tool = ToolName,
            op = "partials",
            pulse = PartialsPulse(partials),
            partials,
            hint = "Seam = TypeName.Topic.cs. topic= suggests next partial name."
        };
    }

    static string BudgetPulse(object budget)
    {
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(budget));
            return doc.RootElement.TryGetProperty("pulse", out var p) ? p.GetString() ?? "" : "";
        }
        catch { return "refactor_plan · budget"; }
    }

    static string BlastPulse(object blast) => BudgetPulse(blast);
    static string PartialsPulse(object partials) => BudgetPulse(partials);

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    static int? OptInt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)) return n;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out n)) return n;
        return null;
    }

    static string Rel(string? root, string path)
    {
        if (root is { Length: > 0 } && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            var r = path[root.Length..].TrimStart('\\', '/');
            if (r.Length > 0) return r;
        }

        return Path.GetFileName(path);
    }

    static string ResolvePath(SessionContext session, string path) =>
        Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(session.ProjectRoot ?? Environment.CurrentDirectory, path));
}
