#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=learn</c> / Meta <c>cdp_learn</c> — Lean dialogue learning desk.
/// Session journal under workspace state; <c>op=promote</c> → agent-notes knowledge (memory_project).
/// Ops: scene|stash|list|recall|promote. Not agent-findings (file memos) and not TM.
/// </summary>
internal static partial class IdeLearnChannel
{
    public const string SchemaVersion = "learn_channel/v0";
    public const string ToolName = "cdp_learn";
    public const string GoName = "learn";
    public const string DefaultPromotePrefix = "work/projects/_learn";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static readonly object Gate = new();

    /// <summary>Optional knowledge writer (file_path relative, markdown body) → status string.</summary>
    static Func<string, string, string>? s_knowledgeWrite;

    public static string JournalPath => Path.Combine(CdpProfile.StateRoot, "learn-journal.jsonl");

    public static void Configure(Func<string, string, string>? knowledgeWrite) =>
        s_knowledgeWrite = knowledgeWrite;

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(session, args), Pretty);

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        var result = op switch
        {
            "scene" or "help" or "status" => Scene(),
            "stash" or "capture" or "note" or "write" => Stash(session, args),
            "list" => List(args),
            "recall" or "get" or "peek" => Recall(args),
            "promote" or "export" => Promote(session, args),
            _ => Fail("unknown_op", "op=scene|stash|list|recall|promote")
        };
        PublishGlass();
        return result;
    }

    public static string PulseLine(SessionContext? session = null)
    {
        _ = session;
        var n = CountEntries();
        return n == 0
            ? "learn · empty · go=learn op=stash"
            : $"learn · {n} card(s) · go=learn";
    }

    /// <summary>Mirror learn journal pulse to flat CIDE chrome latch (not EICAS).</summary>
    public static void PublishGlass()
    {
        try
        {
            var n = CountEntries();
            var pulse = PulseLine();
            // Dark Cockpit: chrome only while learning cards remain in the journal.
            CideLearnLatch.Publish(active: n > 0, pulse, n);
        }
        catch
        {
            /* best-effort */
        }
    }

    static object Scene() => new
    {
        schema = SchemaVersion,
        ok = true,
        op = "scene",
        go = GoName,
        tool = ToolName,
        journal = JournalPath,
        count = CountEntries(),
        ops = new[] { "scene", "stash", "list", "recall", "promote" },
        promote_default = DefaultPromotePrefix + "/{id}.md",
        knowledge_writer = s_knowledgeWrite is not null ? "memory_project" : "local_fallback",
        next = new object[]
        {
            new { go = "learn", label = "Stash", why = "op=stash title= body=" },
            new { go = "learn", label = "List", why = "op=list" },
            new { go = "learn", label = "Promote", why = "op=promote id= [path=]" },
            new { go = "pressure_desk", label = "Pressure", why = "L1 short stash (different)" }
        },
        hint =
            "Lean learning desk: one glance cards from dialogue. " +
            "stash → ws journal (anti-compaction). promote → agent-notes under work/projects/_learn (or path=). " +
            "Not findings (file hash memos) and not TM criteria."
    };


    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => el.ToString()
        };
    }

    static int? OptInt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
            return n;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var p))
            return p;
        return null;
    }
}

