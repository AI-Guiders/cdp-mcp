using System.Text.Json;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// Kind hooks for <c>take</c>: verify + optional ascii preview (diagrammers later).
/// </summary>
internal static class TakeKinds
{
    public sealed record VerifyResult(string Status, int ErrorCount, object? Items, string? Note);
    public sealed record PreviewResult(string Status, string? Ascii, string? Note);

    public static string ResolveFence(DocBuffer buf, string? overrideFence)
    {
        if (overrideFence is { Length: > 0 } f)
            return f.Trim().ToLowerInvariant();

        var lang = buf.Language?.Trim().ToLowerInvariant();
        if (lang is { Length: > 0 } and not "any" and not "text" and not "plaintext")
            return lang switch
            {
                "csharp" => "csharp",
                "typescript" or "javascript" => lang,
                "python" => "python",
                "json" => "json",
                "yaml" or "yml" => "yaml",
                "markdown" or "md" => "markdown",
                _ => lang
            };

        var ext = Path.GetExtension(buf.Path).TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "cs" or "csx" => "csharp",
            "ts" or "tsx" => "typescript",
            "js" or "jsx" => "javascript",
            "py" => "python",
            "json" => "json",
            "yml" or "yaml" => "yaml",
            "md" or "markdown" => "markdown",
            "mmd" or "mermaid" => "mermaid",
            "puml" or "plantuml" => "plantuml",
            "xml" => "xml",
            "html" or "htm" => "html",
            "css" => "css",
            "toml" => "toml",
            "sql" => "sql",
            _ => "text"
        };
    }

    public static string ChatMarkdown(string fence, string body)
    {
        var f = string.IsNullOrWhiteSpace(fence) ? "text" : fence;
        var ticks = "```";
        if (body.Contains(ticks, StringComparison.Ordinal))
            ticks = "````";
        return ticks + f + "\n" + body.TrimEnd('\r', '\n') + "\n" + ticks + "\n";
    }

    public static bool HasIdeDiagnostics(DocBuffer buf)
    {
        var lang = buf.Language?.Trim().ToLowerInvariant();
        if (lang is "csharp" or "typescript")
            return true;
        var ext = Path.GetExtension(buf.Path).ToLowerInvariant();
        return ext is ".cs" or ".csx" or ".ts" or ".tsx";
    }

    public static bool HasKindChecker(DocBuffer buf)
    {
        if (HasIdeDiagnostics(buf))
            return true;
        var ext = Path.GetExtension(buf.Path).ToLowerInvariant();
        return ext is ".json";
    }

    public static VerifyResult VerifyJson(string body)
    {
        try
        {
            using var _ = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "null" : body);
            return new VerifyResult("ok", 0, null, null);
        }
        catch (JsonException ex)
        {
            return new VerifyResult("failed", 1, new object[]
            {
                new { severity = "error", message = ex.Message }
            }, "json_parse");
        }
    }

    public static int CountErrors(object? diagnostics)
    {
        if (diagnostics is null)
            return 0;

        try
        {
            var el = diagnostics is JsonElement je
                ? je
                : JsonSerializer.SerializeToElement(diagnostics);

            if (el.ValueKind == JsonValueKind.Object)
            {
                if (el.TryGetProperty("data", out var data)
                    && data.ValueKind == JsonValueKind.Object
                    && data.TryGetProperty("error_count", out var ec)
                    && ec.TryGetInt32(out var n))
                    return n;

                if (el.TryGetProperty("error_count", out var top)
                    && top.TryGetInt32(out var n2))
                    return n2;

                if (el.TryGetProperty("diagnostics", out var nested))
                    return CountErrors(nested);
            }

            if (el.ValueKind == JsonValueKind.Array)
            {
                var errors = 0;
                foreach (var item in el.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;
                    if (item.TryGetProperty("severity", out var sev)
                        && sev.ValueKind == JsonValueKind.String
                        && string.Equals(sev.GetString(), "error", StringComparison.OrdinalIgnoreCase))
                        errors++;
                }

                return errors;
            }
        }
        catch
        {
            /* best-effort */
        }

        return 0;
    }

    public static PreviewResult PreviewAscii(DocBuffer buf, string body)
    {
        _ = body;
        var ext = Path.GetExtension(buf.Path).ToLowerInvariant();
        var fence = ResolveFence(buf, null);
        if (ext is ".mmd" or ".mermaid" or ".puml" or ".plantuml"
            || fence is "mermaid" or "plantuml")
        {
            return new PreviewResult("skipped", null, "diagram_preview_handler_pending");
        }

        return new PreviewResult("skipped", null, null);
    }
}
