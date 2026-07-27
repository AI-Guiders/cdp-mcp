#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdeRefactorPlanChannel
{
    sealed class Hotspot(string Path, string Metric, string Severity, int Value, int Threshold, string? Symbol, string Message, string? Go)
    {
        public string Path { get; } = Path;
        public string Metric { get; } = Metric;
        public string Severity { get; } = Severity;
        public int Value { get; } = Value;
        public int Threshold { get; } = Threshold;
        public string? Symbol { get; } = Symbol;
        public string Message { get; } = Message;
        public string? Go { get; } = Go;

        public object Card(string? projectRoot) => new
        {
            path = Path,
            rel = Rel(projectRoot, Path),
            metric = Metric,
            severity = Severity,
            value = Value,
            threshold = Threshold,
            symbol = Symbol,
            message = Message,
            go = Go,
            cut = SuggestCut(Metric, Symbol)
        };
    }

    sealed class DebtSnap(IReadOnlyList<Hotspot> Items, string? ProjectRoot)
    {
        public IReadOnlyList<Hotspot> Items { get; } = Items;
        public int Count => Items.Count;
        public string PulseTail => Items.Count == 0
            ? "none"
            : string.Join(", ", Items.Take(3).Select(i => $"{Rel(ProjectRoot, i.Path)}:{i.Metric}={i.Value}"));
        public string PulseLine => $"refactor_plan · debt · {Count} · {PulseTail}";

        public object Card() => new
        {
            count = Count,
            hotspots = Items.Select(i => i.Card(ProjectRoot)).ToList()
        };
    }

    static DebtSnap BuildDebt(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        int max)
    {
        var path = Opt(args, "path") ?? Opt(args, "file_path") ?? Opt(args, "locus");
        var scope = (Opt(args, "scope") ?? (path is { Length: > 0 } ? "file" : "project")).Trim().ToLowerInvariant();
        var list = new List<Hotspot>();

        if ((scope is "file" or "buffer") && path is { Length: > 0 })
        {
            var full = ResolvePath(session, path);
            EnsureOpen(store, full);
            list.AddRange(ParseFindings(QualityGates.EvaluatePath(store, session.ProjectRoot, full)));
        }
        else
        {
            // Open buffers first
            list.AddRange(ParseFindings(QualityGates.EvaluateStore(store, session.ProjectRoot)));

            // Project scan by file_lines — open top candidates for method_lines
            if (session.ProjectRoot is { Length: > 0 } && Directory.Exists(session.ProjectRoot))
            {
                var ranked = RankCsFiles(session.ProjectRoot).Take(12).ToList();
                foreach (var file in ranked)
                {
                    if (list.Any(h => h.Path.Equals(file, StringComparison.OrdinalIgnoreCase))) continue;
                    EnsureOpen(store, file);
                    list.AddRange(ParseFindings(QualityGates.EvaluatePath(store, session.ProjectRoot, file)));
                }
            }
        }

        var ordered = list
            .OrderByDescending(h => h.Severity is "fail" ? 2 : h.Severity is "warn" ? 1 : 0)
            .ThenByDescending(h => h.Value)
            .GroupBy(h => h.Path + "|" + h.Metric + "|" + (h.Symbol ?? ""), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(max)
            .ToList();

        return new DebtSnap(ordered, session.ProjectRoot);
    }

    static IEnumerable<string> RankCsFiles(string root)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var n = f.Replace('\\', '/');
                    return !n.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                           && !n.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                           && !n.Contains("/publish", StringComparison.OrdinalIgnoreCase);
                });
        }
        catch { yield break; }

        foreach (var f in files
                     .Select(f => (Path: f, Lines: QuietLineCount(f)))
                     .Where(x => x.Lines > 0)
                     .OrderByDescending(x => x.Lines)
                     .Select(x => x.Path))
            yield return f;
    }

    static int QuietLineCount(string path)
    {
        try
        {
            var n = 0;
            foreach (var _ in File.ReadLines(path))
            {
                n++;
                if (n > 20000) break;
            }

            return n;
        }
        catch { return 0; }
    }

    static List<Hotspot> ParseFindings(object raw)
    {
        var list = new List<Hotspot>();
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(raw));
            var root = doc.RootElement;
            if (!root.TryGetProperty("findings", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return list;
            foreach (var item in arr.EnumerateArray())
            {
                var metric = item.TryGetProperty("metric", out var m) ? m.GetString() ?? "" : "";
                if (metric is not ("file_lines" or "method_lines" or "suggest_sniper"))
                    continue;
                var path = item.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                if (path.Length == 0) continue;
                var value = item.TryGetProperty("value", out var v) && v.TryGetInt32(out var vn) ? vn : 0;
                var thr = item.TryGetProperty("threshold", out var t) && t.TryGetInt32(out var tn) ? tn : 0;
                list.Add(new Hotspot(
                    path,
                    metric,
                    item.TryGetProperty("severity", out var s) ? s.GetString() ?? "warn" : "warn",
                    value,
                    thr,
                    item.TryGetProperty("symbol", out var sym) ? sym.GetString() : null,
                    item.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "",
                    item.TryGetProperty("go", out var g) ? g.GetString() : null));
            }
        }
        catch { /* ignore */ }

        return list;
    }

    static void EnsureOpen(DocumentBufferStore store, string path)
    {
        try
        {
            if (File.Exists(path))
                store.Open(path);
        }
        catch { /* gates may still report buffer_not_open */ }
    }

    static string SuggestCut(string metric, string? symbol) => metric switch
    {
        "method_lines" when symbol is { Length: > 0 } => $"extract {symbol} → partial/helper",
        "file_lines" => "split TypeName.Topic.cs seam",
        "suggest_sniper" => "go=scope sniper before thick edit",
        _ => "review + extract"
    };
}
