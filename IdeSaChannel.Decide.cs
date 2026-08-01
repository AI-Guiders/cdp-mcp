#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Decide / locus / arg helpers for go=sa_desk.</summary>
internal static partial class IdeSaChannel
{
    static (string Verdict, string Why) Decide(
        GatesSnap gates,
        IdeReviewChannel.FileCard? dirty,
        ClonesSnap? clones)
    {
        var sizeDebt = gates.Findings.Any(f =>
            (f.Id is "file_lines" or "method_lines") && (f.Severity is "fail" or "warn"));
        var hardFail = gates.Fail > 0;
        var cloneHit = clones is { Ok: true, Groups: > 0 };

        if (dirty is { Risk: "secret" })
            return ("need_more", "Dirty path looks secret-sensitive — exclude before refactor ship.");

        if (hardFail && sizeDebt)
            return ("split", "Quality fail on size/method length — extract before more growth.");

        if (sizeDebt && cloneHit)
            return ("split", "Size debt + clone groups — extract shared locus first.");

        if (sizeDebt)
            return ("touch", "Size warn — prefer extract/sniper, not drive-by rewrite.");

        if (cloneHit)
            return ("touch", "Clones present — check correspondence before duplicate edits.");

        if (dirty is { Risk: "high" })
            return ("need_more", "High-risk dirty file — review blast before structural change.");

        if (gates.Warn > 0)
            return ("touch", "Soft quality warns — small moves ok.");

        return ("leave", "No strong refactor signal from gates/dirty/clones.");
    }

    static object[] TakeFindings(GatesSnap gates, int max) =>
        gates.Findings.Take(max).Select(f => (object)new
        {
            id = f.Id,
            severity = f.Severity,
            path = f.Path,
            symbol = f.Symbol,
            message = f.Message,
            go = f.Go
        }).ToArray();

    static object[] BuildNext(Locus locus, string scope, string verdict)
    {
        var list = new List<object>
        {
            new { go = "quality", label = "Quality gates", why = "full findings" },
            new { go = "analysis_scene", label = "Clones / map", why = "feature=clones|semantic_map" },
            new { go = "review", label = "Review dirty", why = "ship risk" }
        };

        if (locus.Line is > 0)
        {
            list.Insert(0, new
            {
                go = "find_usages",
                label = "Blast radius",
                why = $"file_path= line={locus.Line} column={locus.Column ?? 1}"
            });
        }
        else
        {
            list.Insert(0, new
            {
                go = "goto",
                label = "Land locus",
                why = "need line/col for find_usages"
            });
        }

        if (verdict is "split" or "touch")
        {
            list.Insert(0, new
            {
                go = "refactor_plan",
                label = "Recommended next cut",
                why = "op=plan|recommend — one package before extract"
            });
            list.Add(new { go = "scope", label = "Sniper corridor", why = "aim before extract" });
        }

        if (scope != "project")
            list.Add(new { go = "sa_desk", label = "Widen SA", why = "scope=project depth=full" });

        return list.ToArray();
    }

    static Locus ResolveLocus(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var path = Opt(args, "path") ?? Opt(args, "file_path") ?? Opt(args, "locus");
        var line = IntOr(args, "line", null);
        var column = IntOr(args, "column", null);
        var anchor = Opt(args, "anchor") ?? Opt(args, "from") ?? Opt(args, "at");

        if (anchor is { Length: > 0 } && TryParseAnchor(anchor, out var ap, out var al, out var ac))
        {
            path ??= ResolvePath(session, ap);
            line ??= al;
            column ??= ac;
        }

        if (path is not { Length: > 0 })
        {
            var open = store.All.FirstOrDefault();
            if (open is not null)
                path = open.Path;
        }
        else if (!Path.IsPathRooted(path) && session.ProjectRoot is { Length: > 0 })
        {
            path = Path.GetFullPath(Path.Combine(session.ProjectRoot, path));
        }
        else if (path is { Length: > 0 })
        {
            path = Path.GetFullPath(path);
        }

        return new Locus(path, line, column, anchor);
    }

    static string? ResolvePath(SessionContext session, string label)
    {
        if (Path.IsPathRooted(label))
            return Path.GetFullPath(label);
        if (session.ProjectRoot is { Length: > 0 })
        {
            var candidate = Path.Combine(session.ProjectRoot, label.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return label;
    }

    static bool TryParseAnchor(string wire, out string path, out int? line, out int? column)
    {
        path = "";
        line = null;
        column = null;
        // Minimal [F:path;L:n] / [F:path;L:n;C:c]
        if (!wire.Contains("[F:", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            var inner = wire.Trim();
            var f = IndexOfKey(inner, "F:");
            if (f < 0) return false;
            var fEnd = EndOfField(inner, f + 2);
            path = inner[(f + 2)..fEnd].Trim();
            var l = IndexOfKey(inner, "L:");
            if (l >= 0)
            {
                var lEnd = EndOfField(inner, l + 2);
                if (int.TryParse(inner[(l + 2)..lEnd].Trim(), out var ln))
                    line = ln;
            }

            var c = IndexOfKey(inner, "C:");
            if (c >= 0)
            {
                var cEnd = EndOfField(inner, c + 2);
                if (int.TryParse(inner[(c + 2)..cEnd].Trim(), out var col))
                    column = col;
            }

            return path.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    static int IndexOfKey(string s, string key)
    {
        var i = s.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        return i;
    }

    static int EndOfField(string s, int start)
    {
        var semi = s.IndexOf(';', start);
        var br = s.IndexOf(']', start);
        if (semi < 0) return br < 0 ? s.Length : br;
        if (br < 0) return semi;
        return Math.Min(semi, br);
    }

    static IdeReviewChannel.FileCard? FindDirtyForLocus(
        IReadOnlyList<IdeReviewChannel.FileCard> dirty,
        string? path,
        string? projectRoot)
    {
        if (path is not { Length: > 0 } || dirty.Count == 0)
            return null;

        var full = Path.GetFullPath(path);
        foreach (var d in dirty)
        {
            var cand = projectRoot is { Length: > 0 }
                ? Path.GetFullPath(Path.Combine(projectRoot, d.Path.Replace('/', Path.DirectorySeparatorChar)))
                : Path.GetFullPath(d.Path);
            if (full.Equals(cand, StringComparison.OrdinalIgnoreCase))
                return d;
        }

        return null;
    }

    static object LocusCard(Locus locus) => new
    {
        path = locus.Path,
        line = locus.Line,
        column = locus.Column,
        anchor = locus.Anchor
    };

    static object FileCardDto(IdeReviewChannel.FileCard f) => new
    {
        path = f.Path,
        status = f.Status,
        risk = f.Risk,
        why = f.Why
    };

    static string NormDepth(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "pulse" or "p" => "pulse",
        "full" or "raw" or "deep" => "full",
        _ => "slim"
    };

    static string NormScope(string? raw, Locus locus)
    {
        var s = (raw ?? "").Trim().ToLowerInvariant();
        if (s is "buffer" or "file" or "dirty" or "project" or "open")
            return s == "open" ? "buffer" : s;
        return locus.Path is { Length: > 0 } ? "file" : "buffer";
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    static int? IntOr(IReadOnlyDictionary<string, JsonElement> args, string key, int? fallback)
    {
        if (!args.TryGetValue(key, out var el))
            return fallback;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(el.GetString(), out var n) => n,
            _ => fallback
        };
    }

    sealed record Locus(string? Path, int? Line, int? Column, string? Anchor);

    sealed record Finding(string Id, string Severity, string? Path, string? Symbol, string Message, string? Go);

    sealed record GatesSnap(bool Ok, bool Enabled, int Warn, int Fail, string Pulse, IReadOnlyList<Finding> Findings);

    sealed record ClonesSnap(bool Ok, int Groups, string Pulse, object? Sample);
}
