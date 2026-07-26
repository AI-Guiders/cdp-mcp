#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=sa_desk</c> / Meta <c>cdp_sa</c> — agent-native pre-refactor SA (ADR-0010).
/// Axes: locus / scope / depth. Not EICAS <c>go=sa</c>.
/// </summary>
internal static class IdeSaChannel
{
    public const string SchemaVersion = "code_sa/v1";
    public const string ToolName = "cdp_sa";

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

        var depth = NormDepth(Opt(args, "depth") ?? Opt(args, "shape") ?? "slim");
        var locus = ResolveLocus(store, session, args);
        var scope = NormScope(Opt(args, "scope"), locus);

        if (depth == "pulse")
            return PulseOnly(store, session, locus, scope);

        var gates = RunGates(store, session, locus, scope);
        var dirty = IdeReviewChannel.ListDirtyFiles(session.ProjectRoot);
        var dirtyHit = FindDirtyForLocus(dirty, locus.Path, session.ProjectRoot);

        ClonesSnap? clones = null;
        if (depth == "full")
            clones = TryClones(store, session, locus, scope, depth);

        var (verdict, why) = Decide(gates, dirtyHit, clones);
        var topFindings = TakeFindings(gates, depth == "full" ? 12 : 5);
        var pulse = $"sa_desk · {verdict} · {gates.Warn}w/{gates.Fail}f" +
                    (dirtyHit is not null ? $" · dirty:{dirtyHit.Risk}" : "") +
                    (clones is { Groups: > 0 } ? $" · clones:{clones.Groups}" : "");

        var next = BuildNext(locus, scope, verdict);

        if (depth == "full")
        {
            return new
            {
                ok = true,
                schema = SchemaVersion,
                role = "sa_desk",
                go = "sa_desk",
                tool = ToolName,
                detail = "full",
                locus = LocusCard(locus),
                scope,
                depth,
                pulse,
                verdict,
                why,
                quality = new { gates.Ok, gates.Enabled, gates.Warn, gates.Fail, gates.Pulse, findings = topFindings },
                dirty = dirtyHit is null ? null : FileCardDto(dirtyHit),
                dirty_count = dirty.Count,
                clones,
                next,
                hint = "Pre-refactor SA. Verdict is heuristic — confirm blast via find_usages at locus."
            };
        }

        // slim
        return new
        {
            ok = true,
            schema = SchemaVersion,
            role = "sa_desk",
            go = "sa_desk",
            tool = ToolName,
            detail = "slim",
            locus = LocusCard(locus),
            scope,
            depth,
            pulse,
            verdict,
            why,
            quality = new { gates.Warn, gates.Fail, findings = topFindings },
            dirty = dirtyHit is null ? null : FileCardDto(dirtyHit),
            clones = clones is null ? null : new { clones.Ok, clones.Groups, clones.Pulse },
            next,
            hint = "depth=full for clones detail + more findings; go=sa is EICAS (different)."
        };
    }

    static object PulseOnly(
        DocumentBufferStore store,
        SessionContext session,
        Locus locus,
        string scope)
    {
        var gates = RunGates(store, session, locus, scope);
        var dirty = IdeReviewChannel.ListDirtyFiles(session.ProjectRoot);
        var dirtyHit = FindDirtyForLocus(dirty, locus.Path, session.ProjectRoot);
        var (verdict, why) = Decide(gates, dirtyHit, clones: null);
        var pulse = $"sa_desk · {verdict} · {gates.Warn}w/{gates.Fail}f";
        return new
        {
            ok = true,
            schema = SchemaVersion,
            role = "sa_desk",
            go = "sa_desk",
            tool = ToolName,
            detail = "pulse",
            pulse,
            verdict,
            why,
            locus = LocusCard(locus),
            scope,
            next = BuildNext(locus, scope, verdict),
            hint = "depth=slim for findings."
        };
    }

    static GatesSnap RunGates(
        DocumentBufferStore store,
        SessionContext session,
        Locus locus,
        string scope)
    {
        object raw;
        if ((scope is "file" or "buffer") && locus.Path is { Length: > 0 })
        {
            EnsureOpen(store, locus.Path);
            raw = QualityGates.EvaluatePath(store, session.ProjectRoot, locus.Path);
        }
        else
        {
            raw = QualityGates.EvaluateStore(store, session.ProjectRoot);
        }

        return ParseGates(raw);
    }

    static void EnsureOpen(DocumentBufferStore store, string path)
    {
        try
        {
            if (File.Exists(path))
                store.Open(path);
        }
        catch
        {
            // gates may still report buffer_not_open
        }
    }

    static GatesSnap ParseGates(object raw)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(raw));
        var root = doc.RootElement;
        var ok = !root.TryGetProperty("ok", out var okEl) || okEl.ValueKind != JsonValueKind.False;
        var enabled = !root.TryGetProperty("enabled", out var enEl) || enEl.ValueKind != JsonValueKind.False;
        var warn = root.TryGetProperty("warn", out var wEl) && wEl.TryGetInt32(out var wn) ? wn : 0;
        var fail = root.TryGetProperty("fail", out var fEl) && fEl.TryGetInt32(out var fn) ? fn : 0;
        var pulse = root.TryGetProperty("pulse", out var pEl) ? pEl.GetString() ?? "" : "";
        var findings = new List<Finding>();
        if (root.TryGetProperty("findings", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                findings.Add(new Finding(
                    item.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    item.TryGetProperty("severity", out var sev) ? sev.GetString() ?? "" : "",
                    item.TryGetProperty("path", out var path) ? path.GetString() : null,
                    item.TryGetProperty("symbol", out var sym) ? sym.GetString() : null,
                    item.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "",
                    item.TryGetProperty("go", out var go) ? go.GetString() : null));
            }
        }

        return new GatesSnap(ok, enabled, warn, fail, pulse, findings);
    }

    static ClonesSnap? TryClones(
        DocumentBufferStore store,
        SessionContext session,
        Locus locus,
        string scope,
        string depth)
    {
        try
        {
            var cloneScope = (scope is "project" or "dirty") && depth == "full" ? "project" : "file";
            var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["scope"] = JsonSerializer.SerializeToElement(cloneScope),
                ["max_groups"] = JsonSerializer.SerializeToElement(depth == "full" ? 20 : 5),
                ["max_files"] = JsonSerializer.SerializeToElement(depth == "full" ? 200 : 40)
            };
            if (locus.Path is { Length: > 0 })
                dict["path"] = JsonSerializer.SerializeToElement(locus.Path);

            var json = CodeClones.Run(store, session, dict);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            var pulse = root.TryGetProperty("pulse", out var pEl) ? pEl.GetString() ?? "" : "";
            var groups = 0;
            if (root.TryGetProperty("clone_groups", out var gEl) && gEl.ValueKind == JsonValueKind.Array)
                groups = gEl.GetArrayLength();
            else if (root.TryGetProperty("groups", out var g2) && g2.ValueKind == JsonValueKind.Array)
                groups = g2.GetArrayLength();

            object? sample = null;
            if (depth == "full" && root.TryGetProperty("clone_groups", out var fullGroups))
                sample = JsonSerializer.Deserialize<object>(fullGroups.GetRawText());

            return new ClonesSnap(ok, groups, pulse, sample);
        }
        catch (Exception ex)
        {
            return new ClonesSnap(false, 0, $"clones error: {ex.Message}", null);
        }
    }

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
            list.Add(new { go = "scope", label = "Sniper corridor", why = "aim before extract" });

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
