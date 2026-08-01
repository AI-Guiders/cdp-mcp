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
internal static partial class IdeProblemsChannel
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
}
