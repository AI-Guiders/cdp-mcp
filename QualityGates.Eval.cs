using System.Text.RegularExpressions;

namespace CdpMcp;

internal static partial class QualityGates
{
    static List<QualityFinding> EvaluateBuffer(DocBuffer buf, QualityPolicy policy)
    {
        var list = new List<QualityFinding>();
        var lines = CountLines(buf.Text);
        var shortPath = ShortPath(buf.Path);

        if (policy.SuggestSniperFileLines > 0
            && lines >= policy.SuggestSniperFileLines
            && (policy.FileLinesWarn <= 0 || lines < policy.FileLinesWarn)
            && !EditSniper.HasHold)
        {
            list.Add(new QualityFinding(
                "suggest_sniper",
                "warn",
                buf.Path,
                null,
                "file_lines",
                lines,
                policy.SuggestSniperFileLines,
                $"{shortPath}: {lines} lines — harness suggests go=scope before thick edit",
                "go=scope"));
        }

        if (policy.FileLinesFail > 0 && lines >= policy.FileLinesFail)
        {
            list.Add(new QualityFinding(
                "file_lines",
                "fail",
                buf.Path,
                null,
                "file_lines",
                lines,
                policy.FileLinesFail,
                $"{shortPath}: {lines} ≥ fail {policy.FileLinesFail}",
                "go=scope → split / extract"));
        }
        else if (policy.FileLinesWarn > 0 && lines >= policy.FileLinesWarn)
        {
            list.Add(new QualityFinding(
                "file_lines",
                "warn",
                buf.Path,
                null,
                "file_lines",
                lines,
                policy.FileLinesWarn,
                $"{shortPath}: {lines} ≥ warn {policy.FileLinesWarn}",
                "go=scope → consider split"));
        }

        if (string.Equals(buf.Language, "csharp", StringComparison.OrdinalIgnoreCase)
            || buf.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || buf.Path.EndsWith(".csx", StringComparison.OrdinalIgnoreCase))
        {
            QualityFinding? worstMethod = null;
            foreach (var m in ScanCsharpMethods(buf.Text))
            {
                QualityFinding? hit = null;
                if (policy.MethodLinesFail > 0 && m.Lines >= policy.MethodLinesFail)
                {
                    hit = new QualityFinding(
                        "method_lines",
                        "fail",
                        buf.Path,
                        m.Name,
                        "method_lines",
                        m.Lines,
                        policy.MethodLinesFail,
                        $"{shortPath}::{m.Name}: {m.Lines} ≥ fail {policy.MethodLinesFail}",
                        "go=scope from=/till= → extract");
                }
                else if (policy.MethodLinesWarn > 0 && m.Lines >= policy.MethodLinesWarn)
                {
                    hit = new QualityFinding(
                        "method_lines",
                        "warn",
                        buf.Path,
                        m.Name,
                        "method_lines",
                        m.Lines,
                        policy.MethodLinesWarn,
                        $"{shortPath}::{m.Name}: {m.Lines} ≥ warn {policy.MethodLinesWarn}",
                        "go=scope from=/till= → consider extract");
                }

                if (hit is null)
                    continue;
                if (worstMethod is null
                    || hit.Value > worstMethod.Value
                    || (hit.Value == worstMethod.Value && hit.Severity == "fail" && worstMethod.Severity != "fail"))
                    worstMethod = hit;
            }

            if (worstMethod is not null)
                list.Add(worstMethod);
        }

        if (string.Equals(policy.Mode, "warn", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Severity == "fail")
                    list[i] = list[i] with { Severity = "warn", Message = list[i].Message + " (mode=warn → soft)" };
            }
        }

        return list;
    }

    static IEnumerable<(string Name, int Lines)> ScanCsharpMethods(string text)
    {
        // Heuristic: method-like signature then { … matching } — not a full parser.
        var matches = MethodSigRegex().Matches(text);
        foreach (Match m in matches)
        {
            var name = m.Groups["name"].Success ? m.Groups["name"].Value : "method";
            var brace = text.IndexOf('{', m.Index + m.Length - 1);
            if (brace < 0)
                continue;
            var end = MatchingBrace(text, brace);
            if (end < 0)
                continue;
            var slice = text[brace..(end + 1)];
            yield return (name, CountLines(slice));
        }
    }

    static int MatchingBrace(string text, int openIdx)
    {
        var depth = 0;
        for (var i = openIdx; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    static int CountLines(string text)
    {
        if (text.Length == 0) return 1;
        var n = 1;
        foreach (var ch in text)
        {
            if (ch == '\n') n++;
        }

        return n;
    }

    static string ShortPath(string path)
    {
        try
        {
            var name = Path.GetFileName(path);
            var parent = Path.GetFileName(Path.GetDirectoryName(path));
            return string.IsNullOrEmpty(parent) ? name : $"{parent}/{name}";
        }
        catch
        {
            return path;
        }
    }

    [GeneratedRegex(
        """(?m)^\s*(?:public|private|protected|internal|static|async|partial|override|virtual|new|sealed|\s)+[\w<>\[\],\s\?]+\s+(?<name>\w+)\s*\([^;]*\)\s*(?:where\s+[\w\s,:<>]+\s*)?\{""",
        RegexOptions.Compiled)]
    private static partial Regex MethodSigRegex();
}
