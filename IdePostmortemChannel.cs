#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AgentFailures.Core;
using AgentFindings.Core;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=postmortem</c> / Meta <c>cdp_postmortem</c> — ethical blameless incident peel.
/// Template: happened / system_root / why_repeated / fix / do_not. No blame, no chat dump, scrub secrets.
/// Persist: failure store + finding memo + FDR wake-kind event with call_id.
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

    static object Draft(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        bool persist)
    {
        var workspace = ResolveWorkspace(session, args);
        if (workspace is null)
            return Fail("workspace_required", "cdp_open project first, or workspace_path=");

        var draft = BuildDraft(args);
        var scrub = ScrubDraft(draft);
        if (scrub.Refused is { } refuse)
            return new
            {
                schema = SchemaVersion,
                ok = false,
                op = persist ? "record" : "draft",
                go = GoName,
                reason = "ethics_refuse",
                refuse,
                hint = "Integrity exit — rewrite without blame/secrets/chat dump; honesty over silence-as-cover."
            };

        var body = FormatBody(scrub.Draft);
        var fingerprint = scrub.Draft.Fingerprint;
        object? failure = null;
        object? finding = null;
        string? fdrKind = null;

        if (persist)
        {
            var category = ResolveCategory(Opt(args, "category"));
            var view = WorkspaceFailuresStore.Record(
                workspace,
                tool: FailureToolTag,
                errorOrMiss: Truncate(scrub.Draft.Happened, 480),
                argsTried: Truncate(scrub.Draft.WhyRepeated, 480),
                resolution: Truncate(scrub.Draft.Fix, 480),
                correctArgs: Truncate(scrub.Draft.DoNot, 480),
                why: Truncate(scrub.Draft.SystemRoot, 480),
                fingerprint: fingerprint,
                taskId: Opt(args, "task_id"),
                category: category,
                projectId: Opt(args, "project_id"),
                app: "cdp",
                suggestedNext: Truncate(scrub.Draft.DoNot, 240));

            failure = new
            {
                id = view.Record.Id,
                fingerprint = view.Record.Fingerprint,
                deduped = view.Deduped,
                seen_count = view.Record.SeenCount,
                path = WorkspaceFailuresStore.FileForTool(workspace, FailureToolTag)
            };

            var findingPath = FindingPathPrefix + fingerprint + ".md";
            var absFinding = Path.Combine(workspace, findingPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(absFinding)!);
            File.WriteAllText(absFinding, body, Encoding.UTF8);

            var memo = WorkspaceFindingsStore.UpsertMemo(
                workspace,
                path: findingPath,
                contentHash: Sha256Hex(body),
                relevance: "on_task",
                disposition: "leave",
                summary: Truncate(scrub.Draft.Title ?? scrub.Draft.Happened, 200),
                anchors: scrub.Draft.FdrCallId is { Length: > 0 } cid ? "fdr:" + cid : null,
                dependsOnPaths: null,
                taskIds: null,
                status: "active",
                sessionId: null);

            finding = new
            {
                id = memo.Id,
                path = findingPath,
                summary = memo.Summary
            };

            IdeFlightDataRecorder.RecordWake(
                "postmortem",
                scrub.Draft.FdrCallId ?? fingerprint,
                scrub.Draft.Tool ?? FailureToolTag,
                Truncate(scrub.Draft.Title ?? scrub.Draft.Happened, 160));
            fdrKind = "postmortem";
        }

        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = persist ? "record" : "draft",
            go = GoName,
            persisted = persist,
            fingerprint,
            title = scrub.Draft.Title,
            axes = new
            {
                happened = scrub.Draft.Happened,
                system_root = scrub.Draft.SystemRoot,
                why_repeated = scrub.Draft.WhyRepeated,
                fix = scrub.Draft.Fix,
                do_not = scrub.Draft.DoNot
            },
            fdr_call_id = scrub.Draft.FdrCallId,
            tool = scrub.Draft.Tool,
            body_preview = Truncate(body, 1200),
            scrub_notes = scrub.Notes,
            failure,
            finding,
            fdr_kind = fdrKind,
            hint = persist
                ? "Persisted blameless postmortem — failure store + finding memo + FDR. Prefer pulse; do not dump chat."
                : "Draft only — op=record to persist. Review scrub_notes first."
        };
    }

    static object List(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspace = ResolveWorkspace(session, args);
        if (workspace is null)
            return Fail("workspace_required", "cdp_open project first, or workspace_path=");

        var limit = OptInt(args, "limit") ?? 20;
        var list = WorkspaceFailuresStore.List(
            workspace,
            tool: FailureToolTag,
            fingerprint: Opt(args, "fingerprint"),
            category: Opt(args, "category") is { Length: > 0 } cat ? ResolveCategory(cat) : null,
            projectId: null,
            app: "cdp",
            taskId: null,
            latestOnly: true,
            limit: Math.Clamp(limit, 1, 100));

        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "list",
            go = GoName,
            count = list.Count,
            entries = list.Select(v => new
            {
                id = v.Record.Id,
                at = v.Record.AtUtc,
                fingerprint = v.Record.Fingerprint,
                happened = Truncate(v.Record.ErrorOrMiss, 160),
                system_root = Truncate(v.Record.Why, 120),
                fix = Truncate(v.Record.Resolution, 120),
                do_not = Truncate(v.Record.CorrectArgs, 120),
                seen = v.Record.SeenCount
            }).ToArray(),
            hint = "Latest postmortems from failure store (tool=cdp_postmortem)."
        };
    }

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

    internal readonly record struct ScrubResult(
        PostmortemDraft Draft,
        string[] Notes,
        string? Refused);

    internal static PostmortemDraft BuildDraft(IReadOnlyDictionary<string, JsonElement> args)
    {
        var happened = Opt(args, "happened") ?? Opt(args, "what") ?? "";
        var systemRoot = Opt(args, "system_root") ?? Opt(args, "root") ?? "";
        var why = Opt(args, "why_repeated") ?? Opt(args, "why") ?? "";
        var fix = Opt(args, "fix") ?? Opt(args, "resolution") ?? "";
        var doNot = Opt(args, "do_not") ?? Opt(args, "dont") ?? Opt(args, "anti_pattern") ?? "";
        var tool = Opt(args, "tool");
        var fdrCallId = Opt(args, "fdr_call_id") ?? Opt(args, "call_id");
        var title = Opt(args, "title");
        var fpSeed = string.Join("|", new[] { happened, systemRoot, tool }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var fp = Opt(args, "fingerprint")
                 ?? ("pm-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fpSeed)))[..12].ToLowerInvariant());
        return new PostmortemDraft
        {
            Title = title,
            Happened = happened.Trim(),
            SystemRoot = systemRoot.Trim(),
            WhyRepeated = why.Trim(),
            Fix = fix.Trim(),
            DoNot = doNot.Trim(),
            Tool = tool,
            FdrCallId = fdrCallId,
            Fingerprint = fp
        };
    }

    internal static ScrubResult ScrubDraft(PostmortemDraft d)
    {
        var notes = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Happened) || string.IsNullOrWhiteSpace(d.SystemRoot))
            return new ScrubResult(d, notes.ToArray(), "happened + system_root required");

        var joined = string.Join("\n", d.Happened, d.SystemRoot, d.WhyRepeated, d.Fix, d.DoNot);
        if (LooksLikeChatDump(joined))
            return new ScrubResult(d, notes.ToArray(), "chat_dump_refused — summarize facts; do not paste transcript");

        if (LooksLikeBlame(joined))
            return new ScrubResult(d, notes.ToArray(), "blame_language_refused — rewrite as system/mechanism facts");

        var happened = ScrubSecrets(d.Happened, notes);
        var systemRoot = ScrubSecrets(d.SystemRoot, notes);
        var why = ScrubSecrets(d.WhyRepeated, notes);
        var fix = ScrubSecrets(d.Fix, notes);
        var doNot = ScrubSecrets(d.DoNot, notes);

        return new ScrubResult(
            d with
            {
                Happened = Truncate(happened, 800),
                SystemRoot = Truncate(systemRoot, 800),
                WhyRepeated = Truncate(why, 800),
                Fix = Truncate(fix, 800),
                DoNot = Truncate(doNot, 800)
            },
            notes.ToArray(),
            null);
    }

    static string FormatBody(PostmortemDraft d)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Postmortem (blameless)");
        if (!string.IsNullOrWhiteSpace(d.Title))
            sb.AppendLine("## " + d.Title);
        sb.AppendLine();
        sb.AppendLine("## Happened");
        sb.AppendLine(d.Happened);
        sb.AppendLine();
        sb.AppendLine("## System root");
        sb.AppendLine(d.SystemRoot);
        sb.AppendLine();
        sb.AppendLine("## Why repeated");
        sb.AppendLine(string.IsNullOrWhiteSpace(d.WhyRepeated) ? "_(not stated)_" : d.WhyRepeated);
        sb.AppendLine();
        sb.AppendLine("## Fix");
        sb.AppendLine(string.IsNullOrWhiteSpace(d.Fix) ? "_(open)_" : d.Fix);
        sb.AppendLine();
        sb.AppendLine("## Do not");
        sb.AppendLine(string.IsNullOrWhiteSpace(d.DoNot) ? "_(none)_" : d.DoNot);
        if (!string.IsNullOrWhiteSpace(d.FdrCallId))
        {
            sb.AppendLine();
            sb.AppendLine("## FDR");
            sb.AppendLine("call_id: " + d.FdrCallId);
        }

        sb.AppendLine();
        sb.AppendLine("fingerprint: " + d.Fingerprint);
        return sb.ToString();
    }

    static bool LooksLikeChatDump(string text)
    {
        if (text.Length > 6000)
            return true;
        var hits = 0;
        if (ChatDumpCue().IsMatch(text)) hits++;
        if (text.Contains("```", StringComparison.Ordinal) && text.Length > 2500) hits++;
        if (Regex.Matches(text, "\\n").Count > 80) hits++;
        return hits >= 2;
    }

    static bool LooksLikeBlame(string text) =>
        BlameCue().IsMatch(text);

    static string ScrubSecrets(string text, List<string> notes)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        var scrubbed = SecretCue().Replace(text, "[redacted]");
        if (!string.Equals(scrubbed, text, StringComparison.Ordinal))
            notes.Add("secret_pattern_redacted");
        return scrubbed;
    }

    static string ResolveCategory(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)
            || string.Equals(raw, "postmortem", StringComparison.OrdinalIgnoreCase))
            return "unknown";
        try
        {
            return WorkspaceFailuresStore.NormalizeCategory(raw);
        }
        catch (ArgumentException)
        {
            return "unknown";
        }
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
