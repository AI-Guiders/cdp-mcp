#nullable enable
using System.Text.RegularExpressions;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdeRefactorPlanChannel
{
    static readonly Regex RxPartialClass = new(
        @"(?m)^\s*(?:(?:public|internal|private|protected|file|static|abstract|sealed|new)\s+)*partial\s+class\s+(\w+)",
        RegexOptions.Compiled);

    static readonly Regex RxClass = new(
        @"(?m)^\s*(?:(?:public|internal|private|protected|file|static|partial|abstract|sealed|new)\s+)+class\s+(\w+)",
        RegexOptions.Compiled);

    static readonly Regex RxOtherType = new(
        @"(?m)^\s*(?:(?:public|internal|private|protected|file|static|partial|abstract|sealed|readonly|ref)\s+)*(?:record\s+struct|record|struct|interface|enum)\s+(\w+)",
        RegexOptions.Compiled);

    /// <summary>One package: recommended next cut — replaces hand-running debt→budget→partials→sa.</summary>
    static object BuildRecommend(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        DebtSnap debt,
        object? budget,
        object partials)
    {
        _ = budget;
        _ = partials;
        var policy = QualityGates.LoadEffective(session.ProjectRoot);
        var pathArg = Opt(args, "path") ?? Opt(args, "file_path") ?? debt.Items.FirstOrDefault()?.Path;
        if (pathArg is null or { Length: 0 })
        {
            return new
            {
                ok = false,
                error = "path_required",
                pulse = "refactor_plan · recommend · no path",
                hint = "path=File.cs or open a buffer first"
            };
        }

        var full = ResolvePath(session, pathArg);
        EnsureOpen(store, full);
        var shape = DetectShape(full);
        var fileLines = QuietLineCount(full);
        var rel = Rel(session.ProjectRoot, full);

        var methodHits = debt.Items
            .Where(h => h.Path.Equals(full, StringComparison.OrdinalIgnoreCase) && h.Metric == "method_lines")
            .OrderByDescending(h => h.Value)
            .ToList();
        if (methodHits.Count == 0)
        {
            methodHits = ParseFindings(QualityGates.EvaluatePath(store, session.ProjectRoot, full))
                .Where(h => h.Metric == "method_lines")
                .OrderByDescending(h => h.Value)
                .ToList();
        }

        var fileHit = debt.Items.FirstOrDefault(h =>
            h.Path.Equals(full, StringComparison.OrdinalIgnoreCase) && h.Metric == "file_lines");
        if (fileHit is null && fileLines >= policy.FileLinesWarn && policy.FileLinesWarn > 0)
        {
            fileHit = new Hotspot(
                full,
                "file_lines",
                fileLines >= policy.FileLinesFail && policy.FileLinesFail > 0 ? "fail" : "warn",
                fileLines,
                policy.FileLinesFail > 0 ? policy.FileLinesFail : policy.FileLinesWarn,
                null,
                $"{rel}: {fileLines} lines",
                null);
        }

        string verdict;
        string why;
        string pulse;
        object cut;
        object primaryGo;

        if (shape.Kind == "top_level_statements")
        {
            verdict = "design";
            why = "Top-level statements — no cheap TypeName.Topic.cs until partial class Program exists.";
            pulse = "refactor_plan · recommend · design · top_level_statements";
            primaryGo = new { go = "buffer", label = "Open entry file", why = $"cdp_buffer op=open path={rel}" };
            cut = new
            {
                kind = "introduce_program_class",
                cheap = false,
                path = rel,
                steps = new[]
                {
                    "Wrap entry in `partial class Program` (keep top-level only as thin host if needed).",
                    "Then peel Meta/dispatch into Program.Meta.cs via sniper — not before class exists.",
                    "Prefer extract local functions / helpers out of the monster block first if cheaper."
                },
                do_not = "Do not create Program.Meta.cs as orphan partial without a Program type.",
                primary_go = primaryGo
            };
        }
        else if (methodHits.FirstOrDefault() is { } worstMethod
                 && worstMethod.Severity is "fail" or "warn"
                 && PreferMethodCut(fileHit, worstMethod, fileLines, policy))
        {
            var targetAfter = Math.Max(40, policy.MethodLinesWarn > 0 ? policy.MethodLinesWarn - 1 : 79);
            var extractApprox = Math.Max(0, worstMethod.Value - targetAfter);
            verdict = worstMethod.Severity == "fail" ? "split" : "touch";
            why =
                $"Worst method {worstMethod.Symbol ?? "?"} = {worstMethod.Value} lines — extract internals (file peel alone won't clear method_lines).";
            pulse = $"refactor_plan · recommend · {verdict} · method {worstMethod.Symbol}={worstMethod.Value}";
            var topic = SanitizeTopic(worstMethod.Symbol) ?? "Extract";
            var stem = shape.TypeStem ?? Path.GetFileNameWithoutExtension(full);
            string? suggestedPartial = shape.Kind is "class" or "partial_class"
                ? Rel(session.ProjectRoot, Path.Combine(Path.GetDirectoryName(full)!, $"{stem}.{topic}.cs"))
                : null;
            primaryGo = new
            {
                go = "goto",
                label = $"Land {worstMethod.Symbol}",
                why = worstMethod.Symbol is { Length: > 0 }
                    ? $"query={worstMethod.Symbol}"
                    : $"open {rel}"
            };
            cut = new
            {
                kind = "extract_method",
                cheap = true,
                path = rel,
                symbol = worstMethod.Symbol,
                method_lines = worstMethod.Value,
                suggested_after_method_lines = targetAfter,
                extract_lines_approx = extractApprox,
                suggested_partial = suggestedPartial,
                steps = new[]
                {
                    $"Land {worstMethod.Symbol} (goto / symbols).",
                    "go=scope sniper on method body corridors — no thick set_text.",
                    suggestedPartial is not null
                        ? $"Optional: move extracted helpers into {suggestedPartial}."
                        : "Extract local helpers in-file first if type shape is unclear."
                },
                primary_go = primaryGo,
                budget_hint = $"op=budget path={rel} after_method_lines={targetAfter}"
            };
        }
        else if (fileHit is not null && fileHit.Severity is "fail" or "warn")
        {
            var warnTarget = policy.FileLinesWarn > 0 ? policy.FileLinesWarn - 1 : 399;
            var clearWarn = Math.Max(0, fileLines - warnTarget);
            var extract = clearWarn > 0 ? clearWarn : Math.Max(150, fileLines / 4);
            var topic = SanitizeTopic(methodHits.FirstOrDefault()?.Symbol)
                        ?? Opt(args, "topic")
                        ?? GuessTopicFromFile(full)
                        ?? "Slice";
            var stem = shape.TypeStem ?? Path.GetFileNameWithoutExtension(full);
            var suggested = Rel(session.ProjectRoot, Path.Combine(Path.GetDirectoryName(full)!, $"{stem}.{topic}.cs"));
            verdict = fileHit.Severity == "fail" ? "split" : "touch";
            why = $"File {fileLines} lines — peel TypeName.Topic.cs seam (~{extract} lines) via sniper.";
            pulse = $"refactor_plan · recommend · {verdict} · peel {topic} ~{extract}";
            primaryGo = new
            {
                go = "refactor_plan",
                label = "Confirm partials seam",
                why = $"op=partials path={rel} topic={topic}"
            };
            cut = new
            {
                kind = "peel_partial",
                cheap = shape.Kind is "class" or "partial_class",
                path = rel,
                file_lines = fileLines,
                extract_lines = extract,
                suggested_after_file_lines = Math.Max(0, fileLines - extract),
                topic,
                suggested_partial = suggested,
                steps = shape.Kind is "class" or "partial_class"
                    ? new[]
                    {
                        $"cdp_buffer op=create path={suggested} (partial class {stem}).",
                        "Sniper cut/paste members — no thick set_text.",
                        "Re-run go=refactor_plan op=plan — expect method_lines may remain on peeled file."
                    }
                    : new[]
                    {
                        "Shape is not a normal class — do not invent orphan partial; see design path."
                    },
                primary_go = primaryGo,
                budget_hint = $"op=budget path={rel} extract_lines={extract}"
            };
        }
        else
        {
            verdict = "leave";
            why = "No strong size/method signal for this path.";
            pulse = "refactor_plan · recommend · leave";
            primaryGo = new { go = "sa_desk", label = "SA pulse", why = $"path={rel}" };
            cut = new
            {
                kind = "none",
                cheap = true,
                path = rel,
                steps = new[] { "Optional: go=sa_desk for dirty/clones; skip structural cut." },
                primary_go = primaryGo
            };
        }

        return new
        {
            ok = true,
            pulse,
            verdict,
            why,
            shape = new { kind = shape.Kind, type_stem = shape.TypeStem, path = rel },
            cut,
            next = new object[]
            {
                primaryGo,
                new { go = "scope", label = "Sniper corridor", why = "aim before extract" },
                new { go = "sa_desk", label = "SA leave|touch|split", why = "confirm verdict" }
            },
            hint = "One package — act on cut.primary_go; skip hand-running debt→budget→partials unless you need detail."
        };
    }

    static bool PreferMethodCut(
        Hotspot? fileHit,
        Hotspot methodHit,
        int fileLines,
        QualityGates.QualityPolicy policy)
    {
        // After peel: file may be under fail while method_lines still screams — prefer method.
        if (methodHit.Severity == "fail") return true;
        if (fileHit is null) return true;
        if (policy.FileLinesFail > 0 && fileLines < policy.FileLinesFail) return true;
        if (fileHit.Severity == "warn" && methodHit.Value >= policy.MethodLinesWarn) return true;
        return false;
    }

    static (string Kind, string? TypeStem) DetectShape(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            var partial = RxPartialClass.Match(text);
            if (partial.Success)
                return ("partial_class", partial.Groups[1].Value);

            var type = RxClass.Match(text);
            if (type.Success)
                return ("class", type.Groups[1].Value);

            var other = RxOtherType.Match(text);
            if (other.Success)
                return ("type_other", other.Groups[1].Value);

            return ("top_level_statements", Path.GetFileNameWithoutExtension(path));
        }
        catch
        {
            return ("unknown", Path.GetFileNameWithoutExtension(path));
        }
    }

    static string? SanitizeTopic(string? symbol)
    {
        if (symbol is null or { Length: 0 }) return null;
        var s = symbol;
        var tick = s.IndexOf('`');
        if (tick > 0) s = s[..tick];
        var dot = s.LastIndexOf('.');
        if (dot >= 0 && dot < s.Length - 1) s = s[(dot + 1)..];
        if (s.EndsWith("Async", StringComparison.Ordinal) && s.Length > 5)
            s = s[..^5];
        var safe = new string(s.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
        return safe.Length > 0 ? safe : null;
    }

    static string? GuessTopicFromFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^1] : null;
    }
}
