#nullable enable
using System.Text;
using System.Text.RegularExpressions;

namespace CdpMcp;

/// <summary>
/// CIDE-compatible <c>{{ INCLUDE: rel/path }}</c> expansion (ADR 0023).
/// Default <see cref="IncludeScope.All"/> expands INCLUDE lines anywhere (agent modular MD);
/// <see cref="IncludeScope.Fence"/> matches CIDE preview (only inside fenced blocks).
/// </summary>
internal static class MarkdownIncludeExpansion
{
    static readonly Regex IncludeLineRegex = new(
        @"^\s*\{\{\s*include\s*:\s*(?<path>[^}]+?)\s*\}\}\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public enum IncludeScope
    {
        All,
        Fence
    }

    public sealed record Options(int MaxDepth = 5, IncludeScope Scope = IncludeScope.All);

    public sealed class Result
    {
        public required bool Ok { get; init; }
        public string Expanded { get; init; } = "";
        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
        public int IncludeHits { get; init; }
    }

    public static Result TryExpand(string markdown, string markdownFilePath, Options? options = null)
    {
        if (markdown is null)
            return new Result { Ok = true, Expanded = "" };
        if (string.IsNullOrWhiteSpace(markdownFilePath))
            return new Result
            {
                Ok = false,
                Errors = ["INCLUDE: markdownFilePath is required."]
            };

        var opts = options ?? new Options();
        var errors = new List<string>();
        var hits = 0;
        var fullMd = Path.GetFullPath(markdownFilePath);
        var baseDir = Path.GetDirectoryName(fullMd) ?? Directory.GetCurrentDirectory();
        var stack = new Stack<string>();

        var expanded = ExpandMarkdownCore(
            markdown, baseDir, fullMd, opts, stack, depth: 0, errors, ref hits);

        return errors.Count > 0
            ? new Result { Ok = false, Expanded = expanded, Errors = errors, IncludeHits = hits }
            : new Result { Ok = true, Expanded = expanded, IncludeHits = hits };
    }

    public static string DefaultExportPath(string sourcePath)
    {
        try
        {
            var full = Path.GetFullPath(sourcePath);
            var dir = Path.GetDirectoryName(full) ?? Directory.GetCurrentDirectory();
            var name = Path.GetFileNameWithoutExtension(full);
            if (string.IsNullOrWhiteSpace(name))
                name = "export";
            return Path.Combine(dir, $"{name}.expanded.md");
        }
        catch
        {
            return "export.expanded.md";
        }
    }

    static string ExpandMarkdownCore(
        string markdown,
        string baseDir,
        string markdownPathForErrors,
        Options opts,
        Stack<string> stack,
        int depth,
        List<string> errors,
        ref int hits)
    {
        var sb = new StringBuilder(markdown.Length + 128);
        using var reader = new StringReader(markdown);
        string? line;
        var inFence = false;
        string? fenceMarker = null;

        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.TrimStart();
            if (IsFenceStartOrEnd(trimmed, out var marker))
            {
                if (!inFence)
                {
                    inFence = true;
                    fenceMarker = marker;
                }
                else if (fenceMarker is not null && string.Equals(marker, fenceMarker, StringComparison.Ordinal))
                {
                    inFence = false;
                    fenceMarker = null;
                }

                sb.AppendLine(line);
                continue;
            }

            var allow = opts.Scope == IncludeScope.All || inFence;
            if (allow)
            {
                var m = IncludeLineRegex.Match(line);
                if (m.Success)
                {
                    hits++;
                    var rel = (m.Groups["path"].Value ?? "").Trim();
                    var expanded = TryExpandIncludeFile(
                        rel, baseDir, markdownPathForErrors, opts, stack, depth, errors, ref hits);
                    if (expanded is not null)
                    {
                        sb.Append(expanded);
                        if (!expanded.EndsWith('\n'))
                            sb.AppendLine();
                        continue;
                    }
                }
            }

            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    static bool IsFenceStartOrEnd(string trimmedLine, out string marker)
    {
        if (trimmedLine.StartsWith("```", StringComparison.Ordinal))
        {
            marker = "```";
            return true;
        }

        if (trimmedLine.StartsWith("~~~", StringComparison.Ordinal))
        {
            marker = "~~~";
            return true;
        }

        marker = "";
        return false;
    }

    static string? TryExpandIncludeFile(
        string relativePath,
        string baseDir,
        string markdownPathForErrors,
        Options opts,
        Stack<string> stack,
        int depth,
        List<string> errors,
        ref int hits)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            errors.Add($"INCLUDE: empty path in '{markdownPathForErrors}'.");
            return null;
        }

        if (depth >= opts.MaxDepth)
        {
            errors.Add(
                $"INCLUDE: max depth {opts.MaxDepth} exceeded while expanding '{relativePath}' in '{markdownPathForErrors}'.");
            return null;
        }

        var full = Path.GetFullPath(Path.Combine(baseDir, relativePath));
        if (stack.Contains(full, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"INCLUDE: cycle detected: '{full}'.");
            return null;
        }

        if (!File.Exists(full))
        {
            errors.Add($"INCLUDE: file not found: '{full}' (from '{markdownPathForErrors}').");
            return null;
        }

        try
        {
            stack.Push(full);
            var text = File.ReadAllText(full);
            // Included files: line-by-line INCLUDE (diagram/snippet sources).
            return ExpandIncludedText(
                text,
                Path.GetDirectoryName(full) ?? baseDir,
                full,
                opts,
                stack,
                depth + 1,
                errors,
                ref hits);
        }
        catch (Exception ex)
        {
            errors.Add($"INCLUDE: failed to read '{full}': {ex.Message}");
            return null;
        }
        finally
        {
            if (stack.Count > 0 && string.Equals(stack.Peek(), full, StringComparison.OrdinalIgnoreCase))
                stack.Pop();
        }
    }

    static string ExpandIncludedText(
        string text,
        string baseDir,
        string sourcePathForErrors,
        Options opts,
        Stack<string> stack,
        int depth,
        List<string> errors,
        ref int hits)
    {
        var sb = new StringBuilder(text.Length + 64);
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var m = IncludeLineRegex.Match(line);
            if (m.Success)
            {
                hits++;
                var rel = (m.Groups["path"].Value ?? "").Trim();
                var expanded = TryExpandIncludeFile(
                    rel, baseDir, sourcePathForErrors, opts, stack, depth, errors, ref hits);
                if (expanded is not null)
                {
                    sb.Append(expanded);
                    if (!expanded.EndsWith('\n'))
                        sb.AppendLine();
                    continue;
                }
            }

            sb.AppendLine(line);
        }

        return sb.ToString();
    }
}
