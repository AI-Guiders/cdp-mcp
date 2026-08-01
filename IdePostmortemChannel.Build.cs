#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentFailures.Core;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Draft build + scrub helpers for go=postmortem.</summary>
internal static partial class IdePostmortemChannel
{
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
}
