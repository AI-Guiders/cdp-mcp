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

}
