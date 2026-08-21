#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Buffer-plane diagnostics for <c>.ps1</c>: AST parse via pwsh Parser,
/// wired into cdp_buffer edit→diagnose (first-class PS, not opt-in ps1_scene only).
/// </summary>
internal static class Ps1BufferDiagnostics
{
    public const string Kind = "powershell.parser";
    public const string Scope = "syntax";

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static bool IsPs1Path(string? path)
    {
        if (path is not { Length: > 0 }) return false;
        var ext = Path.GetExtension(path);
        return ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".psm1", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".psd1", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<string> DiagnoseAsync(
        string absolutePath,
        string sourceText,
        string? projectRoot,
        CancellationToken cancellationToken = default)
    {
        var pwsh = Ps1PwshRuntime.Resolve();
        if (pwsh is null)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                kind = Kind,
                error = "pwsh_missing",
                hint = "Install PowerShell 7+ (pwsh) on PATH for PS buffer diagnostics."
            }, Json);
        }

        var temp = Path.Combine(Path.GetTempPath(), "cdp-ps1-diag-" + Guid.NewGuid().ToString("N") + ".ps1");
        try
        {
            await File.WriteAllTextAsync(temp, sourceText.Replace("\r\n", "\n"), cancellationToken).ConfigureAwait(false);
            var escaped = temp.Replace("'", "''");
            var cmd =
                "$path='" + escaped + "';" +
                "$errs=$null;$toks=$null;" +
                "[void][System.Management.Automation.Language.Parser]::ParseFile($path,[ref]$toks,[ref]$errs);" +
                "$items=@();" +
                "if($errs){foreach($e in $errs){" +
                "$items+=@{line=$e.Extent.StartLineNumber;column=$e.Extent.StartColumnNumber;" +
                "end_line=$e.Extent.EndLineNumber;end_column=$e.Extent.EndColumnNumber;" +
                "severity='error';id=$e.ErrorId;message=$e.Message}}};" +
                "@{items=$items}|ConvertTo-Json -Compress -Depth 6";

            var cwd = projectRoot is { Length: > 0 } p ? p : Path.GetDirectoryName(absolutePath)!;
            var (exit, stdout, stderr, _) = await Ps1PwshRuntime.RunAsync(
                    pwsh,
                    ["-NoProfile", "-Command", cmd],
                    cwd,
                    30,
                    cancellationToken)
                .ConfigureAwait(false);

            if (exit != 0 && string.IsNullOrWhiteSpace(stdout))
            {
                return JsonSerializer.Serialize(new
                {
                    ok = false,
                    kind = Kind,
                    error = "parser_failed",
                    stderr = Cap(stderr, 2000),
                    hint = "pwsh Parser.ParseFile failed."
                }, Json);
            }

            var wireFile = RelPath(projectRoot, absolutePath);
            var (payloadItems, errorCount) = ParseItems(stdout, absolutePath, wireFile);
            return JsonSerializer.Serialize(new
            {
                ok = true,
                kind = Kind,
                summary = $"scope={Scope} shown={payloadItems.Count} errors={errorCount} cache=miss",
                data = new
                {
                    scope = Scope,
                    cache = "miss",
                    file = absolutePath,
                    shown = payloadItems.Count,
                    error_count = errorCount,
                    truncated = false,
                    items = payloadItems
                }
            }, Json);
        }
        finally
        {
            try { File.Delete(temp); } catch { /* ignore */ }
        }
    }

    static (List<object> Items, int ErrorCount) ParseItems(string stdout, string absolutePath, string wireFile)
    {
        var items = new List<object>();
        var errorCount = 0;
        if (string.IsNullOrWhiteSpace(stdout)) return (items, errorCount);

        try
        {
            using var doc = JsonDocument.Parse(stdout.Trim());
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var arr))
                errorCount += AppendItems(items, arr, absolutePath, wireFile);
            else if (root.ValueKind == JsonValueKind.Array)
                errorCount += AppendItems(items, root, absolutePath, wireFile);
        }
        catch
        {
            items.Add(new
            {
                file = absolutePath,
                line = 1,
                column = 1,
                end_line = 1,
                end_column = 1,
                severity = "error",
                id = "PSParseError",
                message = Cap(stdout.Trim(), 500),
                anchor = $"[F:{wireFile}; L:1]"
            });
            errorCount = 1;
        }

        return (items, errorCount);
    }

    static int AppendItems(List<object> items, JsonElement arr, string absolutePath, string wireFile)
    {
        var errorCount = 0;
        if (arr.ValueKind != JsonValueKind.Array) return errorCount;
        foreach (var el in arr.EnumerateArray())
        {
            var line = el.TryGetProperty("line", out var l) && l.TryGetInt32(out var ln) ? ln : 1;
            var column = el.TryGetProperty("column", out var c) && c.TryGetInt32(out var col) ? col : 1;
            var endLine = el.TryGetProperty("end_line", out var eln) && eln.TryGetInt32(out var elv) ? elv : line;
            var endColumn = el.TryGetProperty("end_column", out var ec) && ec.TryGetInt32(out var ecv) ? ecv : column;
            var severity = el.TryGetProperty("severity", out var s) ? s.GetString() ?? "error" : "error";
            var id = el.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "PSParseError" : "PSParseError";
            var message = el.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            items.Add(new
            {
                file = absolutePath,
                line,
                column,
                end_line = endLine,
                end_column = endColumn,
                severity,
                id,
                message,
                anchor = $"[F:{wireFile}; L:{line}]"
            });
            if (string.Equals(severity, "error", StringComparison.OrdinalIgnoreCase))
                errorCount++;
        }

        return errorCount;
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

    static string Cap(string? text, int max)
    {
        if (text is null) return "";
        return text.Length <= max ? text : text[..max] + "…";
    }
}
