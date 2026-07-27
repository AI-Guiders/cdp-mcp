#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=refactor_plan</c> / Meta <c>cdp_refactor</c> — decide before cut.
/// Axes: debt map · before/after budget · blast next · partials seam.
/// After SA verdict; does not replace go=sa_desk.
/// </summary>
internal static partial class IdeRefactorPlanChannel
{
    public const string Schema = "refactor_plan/v0";
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
        return op switch
        {
            "debt" or "map" or "hotspots" => Debt(store, session, args),
            "budget" or "what_if" => Budget(store, session, args),
            "blast" or "radius" => Blast(args),
            "partials" or "seam" => Partials(session, args),
            "pulse" => Pulse(store, session, args),
            _ => Plan(store, session, args)
        };
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
        var top = debt.Items.FirstOrDefault();
        var pulse = top is null
            ? "refactor_plan · no hotspots in scope"
            : $"refactor_plan · top {Rel(session.ProjectRoot, top.Path)} · {top.Metric}={top.Value}";

        return new
        {
            ok = true,
            schema = Schema,
            go = GoName,
            tool = ToolName,
            op = "plan",
            pulse,
            debt = debt.Card(),
            budget,
            blast,
            partials,
            next = BuildNext(top, blast),
            hint = "Decide: do / skip / defer. Prefer go=scope sniper before extract; sa_desk for leave|touch|split."
        };
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
