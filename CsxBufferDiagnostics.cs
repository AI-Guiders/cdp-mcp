#nullable enable
using System.Text.Json;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// Buffer-plane diagnostics for <c>.csx</c>: ScriptHost allowlist compile,
/// not Roslyn ParseText / MSBuild. Closes the green-buffer / red-check friction.
/// </summary>
internal static class CsxBufferDiagnostics
{
    public const string Kind = "csx.script_host";
    public const string Scope = "csx";

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static bool IsCsxPath(string? path) =>
        path is { Length: > 0 } && path.EndsWith(".csx", StringComparison.OrdinalIgnoreCase);

    public static async Task<string> DiagnoseAsync(
        string absolutePath,
        string sourceText,
        string? projectRoot,
        CancellationToken cancellationToken = default)
    {
        var report = await ScriptHost.CheckAsync(sourceText, cancellationToken).ConfigureAwait(false);
        var items = report.DiagnosticItems ?? [];
        var wireFile = RelPath(projectRoot, absolutePath);

        var payloadItems = items.Select(d =>
        {
            var anchor = d.Anchor is { Length: > 0 }
                ? d.Anchor.Replace(CsxDiagnosticProjection.ScriptFileToken, wireFile, StringComparison.Ordinal)
                : d.Line is int L ? $"[F:{wireFile}; L:{L}]" : $"[F:{wireFile}]";
            return new
            {
                file = absolutePath,
                line = d.Line ?? 0,
                column = d.Column ?? 0,
                end_line = d.Line ?? 0,
                end_column = d.Column ?? 0,
                severity = d.Severity,
                id = d.Id,
                message = d.Message,
                anchor,
                hint = d.Hint
            };
        }).ToArray();

        var errorCount = payloadItems.Length;
        return JsonSerializer.Serialize(new
        {
            ok = true,
            kind = Kind,
            summary = $"scope={Scope} shown={errorCount} errors={errorCount} cache=miss",
            data = new
            {
                scope = Scope,
                cache = "miss",
                file = absolutePath,
                shown = errorCount,
                error_count = errorCount,
                truncated = false,
                items = payloadItems
            }
        }, Json);
    }

    static string RelPath(string? root, string abs)
    {
        if (root is not { Length: > 0 })
            return abs.Replace('\\', '/');
        try
        {
            var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var a = Path.GetFullPath(abs);
            if (a.StartsWith(r, StringComparison.OrdinalIgnoreCase))
                return a[r.Length..].TrimStart('\\', '/').Replace('\\', '/');
        }
        catch
        {
            // keep abs
        }

        return abs.Replace('\\', '/');
    }
}
