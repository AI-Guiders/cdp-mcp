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
