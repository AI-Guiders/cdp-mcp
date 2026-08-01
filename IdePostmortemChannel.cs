#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=postmortem</c> / Meta <c>cdp_postmortem</c> — ethical blameless incident peel.
/// Template: happened / system_root / why_repeated / fix / do_not. No blame, no chat dump, scrub secrets.
/// Persist: failure store + finding memo + FDR wake-kind event with call_id.
/// Draft/list → <c>IdePostmortemChannel.Draft.cs</c>; build/scrub → <c>.Build.cs</c>.
/// </summary>
internal static partial class IdePostmortemChannel
{
    public const string SchemaVersion = "postmortem_channel/v1";
    public const string ToolName = "cdp_postmortem";
    public const string GoName = "postmortem";
    public const string FailureToolTag = "cdp_postmortem";
    public const string FindingPathPrefix = "postmortem/";

    static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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
        return op switch
        {
            "scene" or "help" or "status" => Scene(),
            "template" or "axes" => Template(),
            "draft" or "preview" => Draft(session, args, persist: false),
            "record" or "commit" or "write" => Draft(session, args, persist: true),
            "list" or "recent" => List(session, args),
            _ => Fail("unknown_op", "op=scene|template|draft|record|list")
        };
    }

    public static string PulseLine(SessionContext? session = null)
    {
        _ = session;
        return "postmortem · blameless · go=postmortem op=record";
    }

    static object Scene() => new
    {
        schema = SchemaVersion,
        ok = true,
        op = "scene",
        go = GoName,
        tool = ToolName,
        ops = new[] { "scene", "template", "draft", "record", "list" },
        pulse = PulseLine(),
        ethics = new
        {
            blameless = true,
            no_chat_dump = true,
            scrub_secrets = true,
            integrity = "honesty + exit — not silence"
        },
        next = new object[]
        {
            new { go = "postmortem", label = "Template", why = "op=template — axes" },
            new { go = "postmortem", label = "Draft", why = "op=draft — scrub+preview, no persist" },
            new { go = "postmortem", label = "Record", why = "op=record — failure+finding+FDR" },
            new { go = "fdr", label = "FDR", why = "pair call_id evidence" }
        },
        hint =
            "Ethical SoftOrgan postmortem: happened|system_root|why_repeated|fix|do_not. " +
            "No blame language; scrub secrets/tokens; never paste chat transcripts. " +
            "record → AgentFailures + Findings memo + FDR postmortem event."
    };

    static object Template() => new
    {
        schema = SchemaVersion,
        ok = true,
        op = "template",
        go = GoName,
        axes = new[]
        {
            new { id = "happened", label = "What happened", hint = "Observable fact, no actor blame" },
            new { id = "system_root", label = "System root", hint = "Mechanism / landmine / policy gap" },
            new { id = "why_repeated", label = "Why it repeated", hint = "Incentive, cue, missing gate" },
            new { id = "fix", label = "Fix", hint = "Shipped or planned change" },
            new { id = "do_not", label = "What not to do", hint = "Anti-pattern for next agent" }
        },
        optional = new[] { "tool", "fdr_call_id", "title", "category" },
        hint = "Fill axes; op=draft to scrub; op=record to persist. Exit is legitimate if honesty requires it."
    };

    internal sealed record PostmortemDraft
    {
        public string? Title { get; init; }
        public string Happened { get; init; } = "";
        public string SystemRoot { get; init; } = "";
        public string WhyRepeated { get; init; } = "";
        public string Fix { get; init; } = "";
        public string DoNot { get; init; } = "";
        public string? Tool { get; init; }
        public string? FdrCallId { get; init; }
        public string Fingerprint { get; init; } = "";
    }

    static string? ResolveWorkspace(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var overrideWs = Opt(args, "workspace_path");
        if (!string.IsNullOrWhiteSpace(overrideWs))
            return Path.GetFullPath(overrideWs!);
        if (session.ProjectRoot is { Length: > 0 } pr)
            return Path.GetFullPath(pr);
        if (session.ScmRoot is { Length: > 0 } scm)
            return Path.GetFullPath(scm);
        return null;
    }

    static object Fail(string reason, string hint) => new
    {
        schema = SchemaVersion,
        ok = false,
        go = GoName,
        tool = ToolName,
        reason,
        hint
    };

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
    }

    static int? OptInt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
            return n;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var parsed))
            return parsed;
        return null;
    }

    static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s))
            return s ?? "";
        return s.Length <= max ? s : s[..max] + "…";
    }

    static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    [GeneratedRegex(
        @"(?i)(sk-[a-z0-9]{20,}|ghp_[a-z0-9]{20,}|xox[baprs]-[a-z0-9-]{20,}|api[_-]?key\s*[:=]\s*\S+|password\s*[:=]\s*\S+|Bearer\s+[a-z0-9._\-]{20,}|-----BEGIN [A-Z ]*PRIVATE KEY-----)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SecretCue();

    [GeneratedRegex(
        @"(?i)\b(you (broke|failed|screwed)|my fault|your fault|blame(d|s)? (the )?(agent|operator|human)|incompetent|idiot|stupid (agent|operator))\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex BlameCue();

    [GeneratedRegex(
        @"(?i)(user_query|assistant_message|tool_call|\[Tool (request|result)\]|agent-transcript|jsonl transcript)",
        RegexOptions.CultureInvariant)]
    private static partial Regex ChatDumpCue();
}
