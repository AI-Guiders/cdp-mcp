using System.Text;

namespace CdpMcp;

/// <summary>Reload/Keep/Peek disk drift + GuessLanguage/OffsetOf for DocumentBufferStore (soft-warn peel).</summary>
internal sealed partial class DocumentBufferStore
{
    /// <summary>Force buffer ← disk (VS Reload). Clears dirty.</summary>
    public DocBuffer ReloadFromDisk(string path)
    {
        var full = Path.GetFullPath(path);
        return _gate.Run(full, () => OpenUnlocked(full, refresh: true));
    }

    /// <summary>Reload every open buffer whose disk mtime drifted (batch Reload?).</summary>
    public IReadOnlyList<DocBuffer> ReloadAllDrifted()
    {
        var hits = _byPath.Values.Where(b => b.ProbeDiskChanged(out _, out _)).ToArray();
        foreach (var b in hits)
            ReloadFromDisk(b.Path);
        return hits;
    }

    /// <summary>Keep memory, silence drift pulse (VS Don't Reload).</summary>
    public DocBuffer KeepDisk(string path)
    {
        var full = Path.GetFullPath(path);
        return _gate.Run(full, () =>
        {
            if (!_byPath.TryGetValue(full, out var buf))
                throw new InvalidOperationException($"Buffer not open: {full}");
            buf.AcknowledgeDisk();
            return buf;
        });
    }

    /// <summary>Acknowledge every open buffer with disk drift (batch Don't Reload).</summary>
    public IReadOnlyList<DocBuffer> KeepAllDrifted()
    {
        var hits = _byPath.Values.Where(b => b.ProbeDiskChanged(out _, out _)).ToArray();
        foreach (var b in hits)
            KeepDisk(b.Path);
        return hits;
    }

    /// <summary>Compact memory vs disk peek before Reload? (VS-style glance).</summary>
    public object PeekDisk(string path, int pad = 2, int maxHunkLines = 24)
    {
        var full = Path.GetFullPath(path);
        if (!_byPath.TryGetValue(full, out var buf))
            throw new InvalidOperationException($"Buffer not open: {full}");
        return buf.PeekDisk(pad, maxHunkLines);
    }

    public IReadOnlyList<object> PeekAllDrifted(int pad = 2, int maxHunkLines = 16)
    {
        return _byPath.Values
            .Where(b => b.ProbeDiskChanged(out _, out _))
            .Select(b => b.PeekDisk(pad, maxHunkLines))
            .ToArray();
    }

    public static string GuessLanguage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".cs" or ".csx" => "csharp",
            ".ts" or ".tsx" or ".js" or ".jsx" or ".mjs" or ".cjs" => "typescript",
            ".ps1" or ".psm1" or ".psd1" => "powershell",
            ".py" => "python",
            ".toml" => "toml",
            ".json" or ".jsonc" => "json",
            ".csproj" or ".props" or ".targets" or ".xml" or ".config" or ".xaml" => "xml",
            ".md" or ".markdown" => "markdown",
            _ => "text"
        };
    }

    static int OffsetOf(string text, int line, int column)
    {
        var lineIdx = 1;
        var i = 0;
        while (i < text.Length && lineIdx < line)
        {
            if (text[i] == '\n')
                lineIdx++;
            i++;
        }

        if (lineIdx != line)
            throw new ArgumentException($"Line {line} is past end of buffer ({lineIdx} lines).");

        // column is 1-based char offset within line (UTF-16 code units, same as typical IDE).
        var col = 1;
        while (i < text.Length && col < column)
        {
            if (text[i] == '\n')
                break;
            i++;
            col++;
        }

        if (col != column)
            throw new ArgumentException($"Column {column} is past end of line {line}.");
        return i;
    }
}
