#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=problems</c> — human Error List on seat P.
/// Rows carry code anchors; tap → sniper aim (scope+peek), not path:line dump.
/// Source: cached buffer diagnostics (LastDiagnosticsJson). Orthogonal to quality gates.
/// </summary>
internal static class IdeProblemsChannel
{
    public const string SchemaVersion = "problems_channel/v1";
    public const int MaxRows = 48;

    public sealed record Row(
        string Id,
        string Severity,
        string Message,
        string? Code,
        string Path,
        int Line,
        int EndLine,
        string Anchor,
        string? DocId,
        bool Stale);

    public sealed record Snap(
        bool Ok,
        string Pulse,
        int Errors,
        int Warnings,
        int Infos,
        int BufferCount,
        int DiagnosedCount,
        IReadOnlyList<Row> Rows);

    public static Snap Build(DocumentBufferStore store, SessionContext session)
    {
        var rows = new List<Row>();
        var errors = 0;
        var warns = 0;
        var infos = 0;
        var diagnosed = 0;
        var root = session.ProjectRoot;

        foreach (var buf in store.All.OrderBy(b => b.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (buf.LastDiagnosticsJson is not { Length: > 0 })
                continue;

            diagnosed++;
            var stale = buf.LastDiagnosedVersion != buf.Version;
            if (!TryParseItems(buf.LastDiagnosticsJson, buf.Path, root, out var items))
                continue;

            foreach (var item in items)
            {
                var sev = NormalizeSeverity(item.Severity);
                switch (sev)
                {
                    case "error": errors++; break;
                    case "warning": warns++; break;
                    default: infos++; break;
                }

                if (rows.Count >= MaxRows)
                    continue;

                var line = Math.Max(1, item.Line);
                var end = Math.Max(line, item.EndLine);
                var anchor = item.Anchor is { Length: > 0 }
                    ? item.Anchor
                    : FormatAnchor(root, buf.Path, line, end);
                var id = $"p{rows.Count + 1}";
                rows.Add(new Row(
                    id,
                    sev,
                    Trunc(item.Message, 160),
                    item.Code,
                    buf.Path,
                    line,
                    end,
                    anchor,
                    buf.DocId,
                    stale));
            }
        }

        var ok = errors == 0;
        var pulse = errors > 0
            ? $"problems ERR×{errors}" + (warns > 0 ? $" WARN×{warns}" : "")
            : warns > 0
                ? $"problems WARN×{warns}"
                : diagnosed == 0
                    ? "problems · no diags (buffer diagnostics first)"
                    : "problems · clear";

        return new Snap(ok, pulse, errors, warns, infos, store.All.Count, diagnosed, rows);
    }

    public static object Handle(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        var merged = FlattenArgs(args);
        var snap = Build(store, session);

        object? aim = null;
        string? aimWire = null;
        Row? picked = null;

        var wire = Opt(merged, "wire") ?? Opt(merged, "anchor") ?? Opt(merged, "at");
        var rowKey = Opt(merged, "row") ?? Opt(merged, "id") ?? Opt(merged, "pick");
        var aimWanted = BoolOr(merged, "aim", defaultValue: true)
            && (wire is { Length: > 0 } || rowKey is { Length: > 0 });

        if (aimWanted)
        {
            if (wire is { Length: > 0 })
            {
                aimWire = wire;
                picked = snap.Rows.FirstOrDefault(r =>
                    string.Equals(r.Anchor, wire, StringComparison.OrdinalIgnoreCase));
            }
            else if (TryPickRow(snap.Rows, rowKey!, out var row))
            {
                picked = row;
                aimWire = row.Anchor;
            }

            if (aimWire is { Length: > 0 })
            {
                var pad = IntOr(merged, "pad", 2);
                aim = EditSniper.AimAtWire(store, session, aimWire, pad);
            }
            else
            {
                aim = new
                {
                    ok = false,
                    error = "row_not_found",
                    hint = "row=1..n or wire=[F:…;L:…] from rows[]."
                };
            }
        }

        var lines = snap.Rows
            .Take(24)
            .Select(r =>
                $"{Glyph(r.Severity)}{r.Id} {Path.GetFileName(r.Path)}:{r.Line} {r.Message}")
            .ToArray();
        if (lines.Length == 0)
            lines = [snap.DiagnosedCount == 0
                ? "(no cached diags — cdp_buffer op=diagnostics on open files)"
                : "(clear)"];

        return new
        {
            ok = snap.Ok && (aim is null || AimOk(aim)),
            schema = SchemaVersion,
            role = "problems",
            go = "problems",
            detail = "list",
            pulse = snap.Pulse,
            counts = new
            {
                errors = snap.Errors,
                warnings = snap.Warnings,
                infos = snap.Infos,
                rows = snap.Rows.Count,
                buffers = snap.BufferCount,
                diagnosed = snap.DiagnosedCount,
                truncated = snap.Errors + snap.Warnings + snap.Infos > snap.Rows.Count
            },
            view = new { schema = SchemaVersion, lines },
            rows = snap.Rows.Select(r => new
            {
                id = r.Id,
                severity = r.Severity,
                message = r.Message,
                code = r.Code,
                path = Rel(root: session.ProjectRoot, r.Path),
                line = r.Line,
                end_line = r.EndLine,
                anchor = r.Anchor,
                doc_id = r.DocId,
                stale = r.Stale,
                go = "problems",
                go_args = new { row = r.Id, aim = true }
            }).ToArray(),
            picked = picked is null
                ? null
                : new { id = picked.Id, anchor = picked.Anchor, severity = picked.Severity },
            aim,
            sniper = EditSniper.HoldCard(),
            next = BuildNext(snap, picked),
            hint = snap.Rows.Count == 0
                ? "Empty list. Warm: open buffers → diagnostics; then go=problems. Gates stay go=quality."
                : "Tap: go=problems go_args.row=p1 (or wire=). Aims sniper corridor+peek — not path:line chat."
        };
    }

    public static object PulseCard(Snap snap) => new
    {
        schema = SchemaVersion,
        ok = snap.Ok,
        pulse = snap.Pulse,
        errors = snap.Errors,
        warnings = snap.Warnings
    };

    static object[] BuildNext(Snap snap, Row? picked)
    {
        var list = new List<object>();
        if (picked is not null)
        {
            list.Add(new { go = "target", label = "Outline aim", why = "sniper corridor held" });
            list.Add(new { go = "edit_draft", label = "Shoot", why = "fix at aimed span" });
            list.Add(new { go = "peek", label = "Re-peek", why = "tiny window again" });
        }
        else if (snap.Rows.Count > 0)
        {
            list.Add(new
            {
                go = "problems",
                label = $"Aim {snap.Rows[0].Id}",
                why = "go_args.row=" + snap.Rows[0].Id
            });
        }

        list.Add(new { go = "quality", label = "Gates", why = "policy — not compiler list" });
        list.Add(new { go = "buffer_scene", label = "Buffers", why = "diagnose open docs" });
        return list.ToArray();
    }

    static bool AimOk(object aim)
    {
        try
        {
            var json = JsonSerializer.Serialize(aim);
            using var doc = JsonDocument.Parse(json);
            return !doc.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.False;
        }
        catch
        {
            return true;
        }
    }

    static bool TryPickRow(IReadOnlyList<Row> rows, string key, out Row row)
    {
        row = default!;
        var k = key.Trim();
        if (k.Length == 0)
            return false;

        if (int.TryParse(k, out var n) && n >= 1 && n <= rows.Count)
        {
            row = rows[n - 1];
            return true;
        }

        if (k.StartsWith('p') && int.TryParse(k[1..], out n) && n >= 1 && n <= rows.Count)
        {
            row = rows[n - 1];
            return true;
        }

        var hit = rows.FirstOrDefault(r =>
            string.Equals(r.Id, k, StringComparison.OrdinalIgnoreCase));
        if (hit is null)
            return false;
        row = hit;
        return true;
    }

    sealed record ParsedItem(
        string Severity,
        string Message,
        string? Code,
        int Line,
        int EndLine,
        string? Anchor);

    static bool TryParseItems(
        string json,
        string bufferPath,
        string? projectRoot,
        out List<ParsedItem> items)
    {
        items = [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            foreach (var arr in FindItemArrays(root))
            {
                foreach (var el in arr.EnumerateArray())
                    if (TryMapItem(el, bufferPath, projectRoot, out var item))
                        items.Add(item);
            }

            return items.Count > 0 || root.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    static IEnumerable<JsonElement> FindItemArrays(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var name in new[] { "items", "diagnostics", "Diagnostics" })
        {
            if (root.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
                yield return arr;
        }

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "items", "diagnostics" })
            {
                if (data.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    yield return arr;
            }
        }

        if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "items", "diagnostics" })
            {
                if (result.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    yield return arr;
            }
        }
    }

    static bool TryMapItem(JsonElement el, string bufferPath, string? projectRoot, out ParsedItem item)
    {
        item = default!;
        if (el.ValueKind != JsonValueKind.Object)
            return false;

        var message = PropString(el, "message") ?? PropString(el, "Message") ?? "";
        if (message.Length == 0)
            return false;

        var severity = PropString(el, "severity") ?? PropString(el, "Severity") ?? "info";
        var code = PropString(el, "id") ?? PropString(el, "code") ?? PropString(el, "Code");
        var anchor = PropString(el, "anchor") ?? PropString(el, "Anchor");

        var line = PropInt(el, "line")
            ?? PropInt(el, "Line")
            ?? PropInt(el, "start_line")
            ?? 0;
        var endLine = PropInt(el, "end_line")
            ?? PropInt(el, "EndLine")
            ?? line;

        if (el.TryGetProperty("range", out var range) && range.ValueKind == JsonValueKind.Object)
        {
            line = PropInt(range, "start_line") ?? PropInt(range, "StartLine") ?? line;
            endLine = PropInt(range, "end_line") ?? PropInt(range, "EndLine") ?? endLine;
        }

        if (line <= 0 && anchor is { Length: > 0 })
        {
            try
            {
                var span = BracketLocate.Parse(anchor);
                line = span.LineStart ?? 0;
                endLine = span.LineEnd ?? line;
            }
            catch
            {
                // keep 0
            }
        }

        if (line <= 0)
            line = 1;
        if (endLine < line)
            endLine = line;

        if (anchor is null or { Length: 0 })
            anchor = FormatAnchor(projectRoot, bufferPath, line, endLine);

        item = new ParsedItem(severity, message, code, line, endLine, anchor);
        return true;
    }

    static string FormatAnchor(string? root, string absolutePath, int line, int endLine)
    {
        var label = Rel(root, absolutePath).Replace('\\', '/');
        return BracketLocate.Format(new BracketLocate.Span(
            label,
            null,
            line,
            endLine == line ? null : endLine));
    }

    static string Rel(string? root, string abs)
    {
        if (root is not { Length: > 0 })
            return abs.Replace('\\', '/');
        try
        {
            var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var a = Path.GetFullPath(abs);
            if (a.StartsWith(r, StringComparison.OrdinalIgnoreCase))
            {
                var rel = a[r.Length..].TrimStart('\\', '/');
                return rel.Replace('\\', '/');
            }
        }
        catch
        {
            // fall through
        }

        return Path.GetFileName(abs);
    }

    static string NormalizeSeverity(string raw)
    {
        var s = raw.Trim().ToLowerInvariant();
        return s switch
        {
            "0" or "error" or "err" or "fatal" or "critical" => "error",
            "1" or "warning" or "warn" => "warning",
            "2" or "info" or "information" or "hint" or "note" => "info",
            _ when s.Contains("error", StringComparison.Ordinal) => "error",
            _ when s.Contains("warn", StringComparison.Ordinal) => "warning",
            _ => "info"
        };
    }

    static string Glyph(string severity) => severity switch
    {
        "error" => "!",
        "warning" => "*",
        _ => "·"
    };

    static string Trunc(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    static Dictionary<string, JsonElement> FlattenArgs(IReadOnlyDictionary<string, JsonElement>? args)
    {
        var merged = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (args is null)
            return merged;

        foreach (var kv in args)
        {
            if (kv.Key is "go_args" && kv.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in kv.Value.EnumerateObject())
                    merged[p.Name] = p.Value.Clone();
                continue;
            }

            merged[kv.Key] = kv.Value.Clone();
        }

        return merged;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.ToString(),
            _ => null
        };
    }

    static int IntOr(IReadOnlyDictionary<string, JsonElement> args, string key, int fallback)
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

    static bool BoolOr(IReadOnlyDictionary<string, JsonElement> args, string key, bool defaultValue)
    {
        if (!args.TryGetValue(key, out var el))
            return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(el.GetString(), out var b) => b,
            _ => defaultValue
        };
    }

    static string? PropString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    static int? PropInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n))
            return n;
        if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out n))
            return n;
        return null;
    }
}
