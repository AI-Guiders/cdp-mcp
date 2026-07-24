using System.Text;

namespace CdpMcp;

/// <summary>
/// Session document buffers — agent edits go here; flush + diagnose ≈ almost-online LSP.
/// Disk mutates go through <see cref="PathMutateGate"/> + <see cref="AtomicTextFile"/> so parallel
/// agent tool calls stay comfortable: different paths concurrent, same path serialized.
/// </summary>
internal sealed class DocumentBufferStore
{
    private readonly Dictionary<string, DocBuffer> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly PathMutateGate _gate = new();
    private int _nextId = 1;

    public IReadOnlyCollection<DocBuffer> All => _byPath.Values;

    /// <summary>Run work under the per-path mutate gate (edit+flush as one critical section).</summary>
    public Task<T> MutateAsync<T>(string path, Func<Task<T>> work, CancellationToken cancellationToken = default) =>
        _gate.RunAsync(path, work, cancellationToken);

    public DocBuffer Open(string path, bool refresh = false)
    {
        var full = Path.GetFullPath(path);
        return _gate.Run(full, () => OpenUnlocked(full, refresh));
    }

    DocBuffer OpenUnlocked(string full, bool refresh)
    {
        if (!File.Exists(full))
            throw new FileNotFoundException($"File not found: {full}", full);

        if (_byPath.TryGetValue(full, out var existing))
        {
            // Re-open from disk if not dirty (external edit); keep dirty buffer unless refresh.
            if (refresh || !existing.Dirty)
            {
                existing.Text = File.ReadAllText(full);
                existing.Version++;
                existing.DiskMtimeUtc = File.GetLastWriteTimeUtc(full);
                existing.Language = GuessLanguage(full);
                if (refresh)
                    existing.Dirty = false;
            }

            return existing;
        }

        var text = File.ReadAllText(full);
        var buf = new DocBuffer
        {
            DocId = $"doc-{_nextId++}",
            Path = full,
            Text = text,
            Version = 1,
            Dirty = false,
            Language = GuessLanguage(full),
            DiskMtimeUtc = File.GetLastWriteTimeUtc(full)
        };
        _byPath[full] = buf;
        return buf;
    }

    /// <summary>Create a new file (or overwrite when overwrite=true) and open it as a buffer.</summary>
    public DocBuffer Create(string path, string? initialText = null, bool overwrite = false)
    {
        var full = Path.GetFullPath(path);
        return _gate.Run(full, () =>
        {
            if (File.Exists(full) && !overwrite)
                throw new InvalidOperationException($"File already exists: {full}. Pass overwrite=true or op=open.");
            var text = initialText ?? "";
            AtomicTextFile.WriteUtf8(full, text);
            _byPath.Remove(full);
            var buf = new DocBuffer
            {
                DocId = $"doc-{_nextId++}",
                Path = full,
                Text = text,
                Version = 1,
                Dirty = false,
                Language = GuessLanguage(full),
                DiskMtimeUtc = File.GetLastWriteTimeUtc(full)
            };
            _byPath[full] = buf;
            return buf;
        });
    }

    public DocBuffer Resolve(string? path, string? docId)
    {
        if (docId is { Length: > 0 })
        {
            var hit = _byPath.Values.FirstOrDefault(b =>
                string.Equals(b.DocId, docId, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                return hit;
            throw new ArgumentException($"Unknown doc_id: {docId}");
        }

        if (path is not { Length: > 0 })
            throw new ArgumentException("path or doc_id is required.");

        var full = Path.GetFullPath(path);
        if (_byPath.TryGetValue(full, out var buf))
            return buf;
        return Open(full);
    }

    /// <summary>
    /// Resolve/open while the caller already holds <see cref="MutateAsync"/> for this path.
    /// Nested <see cref="Open"/> would Wait the same <see cref="PathMutateGate"/> → deadlock.
    /// </summary>
    internal DocBuffer ResolveUnlocked(string? path, string? docId)
    {
        if (docId is { Length: > 0 })
        {
            var hit = _byPath.Values.FirstOrDefault(b =>
                string.Equals(b.DocId, docId, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                return hit;
            throw new ArgumentException($"Unknown doc_id: {docId}");
        }

        if (path is not { Length: > 0 })
            throw new ArgumentException("path or doc_id is required.");

        return OpenUnlocked(Path.GetFullPath(path), refresh: false);
    }

    public bool TryGet(string pathOrId, out DocBuffer buf)
    {
        if (_byPath.TryGetValue(Path.GetFullPath(pathOrId), out buf!))
            return true;
        buf = _byPath.Values.FirstOrDefault(b =>
            string.Equals(b.DocId, pathOrId, StringComparison.OrdinalIgnoreCase))!;
        return buf is not null;
    }

    public bool Close(string? path, string? docId)
    {
        var buf = Resolve(path, docId);
        return _byPath.Remove(buf.Path);
    }

    public void ApplySetText(DocBuffer buf, string text)
    {
        buf.Text = text ?? "";
        buf.Version++;
        buf.Dirty = true;
    }

    public void ApplyReplace(DocBuffer buf, string oldString, string newString)
    {
        if (string.IsNullOrEmpty(oldString))
            throw new ArgumentException("old_string is required for replace.");

        if (!TryFindUniqueSpan(buf.Text, oldString, out var idx, out var matchedLen))
        {
            // Distinguish not-found vs ambiguous for agent recovery.
            var exact = buf.Text.IndexOf(oldString, StringComparison.Ordinal);
            if (exact < 0
                && buf.Text.IndexOf(NormalizeNewlines(oldString, "\n"), StringComparison.Ordinal) < 0
                && buf.Text.IndexOf(NormalizeNewlines(oldString, "\r\n"), StringComparison.Ordinal) < 0)
                throw new ArgumentException("old_string not found in buffer.");
            throw new ArgumentException("old_string is not unique in buffer; narrow the span.");
        }

        var insertion = AdaptNewlinesToBuffer(newString ?? "", buf.Text);
        buf.Text = string.Concat(buf.Text.AsSpan(0, idx), insertion, buf.Text.AsSpan(idx + matchedLen));
        buf.Version++;
        buf.Dirty = true;
    }

    /// <summary>
    /// Exact Ordinal match first; if miss, retry with LF/CRLF variants of <paramref name="needle"/>
    /// so agents can pass \\n while the buffer still has Windows CRLF.
    /// </summary>
    static bool TryFindUniqueSpan(string haystack, string needle, out int index, out int matchedLength)
    {
        index = -1;
        matchedLength = 0;
        string[] candidates =
        [
            needle,
            NormalizeNewlines(needle, "\n"),
            NormalizeNewlines(needle, "\r\n")
        ];

        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            if (candidate.Length == 0)
                continue;
            var first = haystack.IndexOf(candidate, StringComparison.Ordinal);
            if (first < 0)
                continue;
            var second = haystack.IndexOf(candidate, first + candidate.Length, StringComparison.Ordinal);
            if (second >= 0)
                return false;
            index = first;
            matchedLength = candidate.Length;
            return true;
        }

        return false;
    }

    static string NormalizeNewlines(string text, string eol) =>
        (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", eol, StringComparison.Ordinal);

    static string AdaptNewlinesToBuffer(string text, string bufferSample)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('\n') < 0 && text.IndexOf('\r') < 0)
            return text;
        var eol = bufferSample.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return NormalizeNewlines(text, eol);
    }

    /// <summary>1-based line/column; end exclusive on end position (like LSP range).</summary>
    public void ApplyReplaceRange(
        DocBuffer buf,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn,
        string text)
    {
        if (startLine < 1 || startColumn < 1 || endLine < 1 || endColumn < 1)
            throw new ArgumentException("line/column are 1-based and must be >= 1.");
        var start = OffsetOf(buf.Text, startLine, startColumn);
        var end = OffsetOf(buf.Text, endLine, endColumn);
        if (end < start)
            throw new ArgumentException("end position is before start.");
        buf.Text = string.Concat(buf.Text.AsSpan(0, start), text ?? "", buf.Text.AsSpan(end));
        buf.Version++;
        buf.Dirty = true;
    }

    public void Flush(DocBuffer buf, bool allowShrink = false) =>
        _gate.Run(buf.Path, () => FlushUnlocked(buf, allowShrink));

    /// <summary>
    /// Flush without taking the gate — caller must already hold <see cref="MutateAsync"/> for this path.
    /// Policy (no magic size thresholds): shrinking an existing file on disk requires explicit
    /// <paramref name="allowShrink"/> — full rewrites that get shorter are intent, not heuristics.
    /// </summary>
    internal void FlushUnlocked(DocBuffer buf, bool allowShrink = false)
    {
        if (File.Exists(buf.Path) && !allowShrink)
        {
            var diskLen = new FileInfo(buf.Path).Length;
            var bodyLen = buf.Text.Length;
            if (bodyLen < diskLen)
            {
                throw new InvalidOperationException(
                    $"Refusing shrink flush of '{buf.Path}' ({diskLen} → {bodyLen} bytes). " +
                    "Pass allow_shrink=true when the shorter body is intentional " +
                    "(or use create overwrite=true / delete outside the buffer).");
            }
        }

        AtomicTextFile.WriteUtf8(buf.Path, buf.Text);
        buf.Dirty = false;
        buf.DiskMtimeUtc = File.GetLastWriteTimeUtc(buf.Path);
    }

    public object Scene()
    {
        var docs = _byPath.Values
            .OrderBy(b => b.DocId, StringComparer.Ordinal)
            .Select(b => b.ToMeta())
            .ToArray();
        var drift = _byPath.Values.Count(b => b.ProbeDiskChanged(out _, out _));
        return new
        {
            schema = "doc_scene/v0",
            count = _byPath.Count,
            dirty_count = _byPath.Values.Count(b => b.Dirty),
            disk_changed_count = drift,
            docs
        };
    }

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

internal sealed class DocBuffer
{
    public required string DocId { get; init; }
    public required string Path { get; init; }
    public required string Text { get; set; }
    public int Version { get; set; }
    public bool Dirty { get; set; }
    public required string Language { get; set; }
    public DateTime DiskMtimeUtc { get; set; }
    public string? LastDiagnosticsJson { get; set; }
    public DateTime? LastDiagnosedUtc { get; set; }
    /// <summary>Buffer version when <see cref="LastDiagnosticsJson"/> was computed.</summary>
    public int? LastDiagnosedVersion { get; set; }
    public string? LastDiagnosedScope { get; set; }

    public object ToMeta()
    {
        var changed = ProbeDiskChanged(out var diskNow, out var reason);
        return new
        {
            doc_id = DocId,
            path = Path,
            language = Language,
            version = Version,
            dirty = Dirty,
            line_count = CountLines(Text),
            char_count = Text.Length,
            disk_mtime_utc = DiskMtimeUtc,
            disk_now_utc = diskNow,
            disk_changed = changed,
            disk_changed_reason = reason,
            last_diagnosed_utc = LastDiagnosedUtc
        };
    }

    /// <summary>
    /// VS-style "File Modified Outside the Environment": disk mtime (or presence)
    /// differs from last buffer sync.
    /// </summary>
    public bool ProbeDiskChanged(out DateTime? diskNow, out string? reason)
    {
        diskNow = null;
        reason = null;
        try
        {
            if (!File.Exists(Path))
            {
                reason = "missing_on_disk";
                return true;
            }

            diskNow = File.GetLastWriteTimeUtc(Path);
            if (diskNow.Value != DiskMtimeUtc)
            {
                reason = "mtime";
                return true;
            }

            return false;
        }
        catch (Exception)
        {
            reason = "probe_failed";
            return true;
        }
    }

    /// <summary>Silence drift without taking disk (Don't Reload).</summary>
    public void AcknowledgeDisk()
    {
        if (File.Exists(Path))
            DiskMtimeUtc = File.GetLastWriteTimeUtc(Path);
    }

    /// <summary>Glance: memory vs on-disk text (not a full unified dump).</summary>
    public object PeekDisk(int pad = 2, int maxHunkLines = 24)
    {
        pad = Math.Clamp(pad, 0, 8);
        maxHunkLines = Math.Clamp(maxHunkLines, 4, 80);
        var changed = ProbeDiskChanged(out var diskNow, out var reason);
        if (!File.Exists(Path))
        {
            return new
            {
                schema = "doc_disk_peek/v0",
                path = Path,
                doc_id = DocId,
                disk_changed = changed,
                disk_changed_reason = reason ?? "missing_on_disk",
                content_same = false,
                missing_on_disk = true,
                dirty = Dirty,
                disk_mtime_utc = DiskMtimeUtc,
                disk_now_utc = diskNow
            };
        }

        var diskText = File.ReadAllText(Path);
        var contentSame = string.Equals(Text, diskText, StringComparison.Ordinal);
        if (contentSame)
        {
            return new
            {
                schema = "doc_disk_peek/v0",
                path = Path,
                doc_id = DocId,
                disk_changed = changed,
                disk_changed_reason = reason,
                content_same = true,
                missing_on_disk = false,
                dirty = Dirty,
                pulse = changed ? "mtime drifted, content same" : "in sync",
                disk_mtime_utc = DiskMtimeUtc,
                disk_now_utc = diskNow,
                mem_lines = CountLines(Text),
                disk_lines = CountLines(diskText)
            };
        }

        var mem = SplitLines(Text);
        var disk = SplitLines(diskText);
        var first = 0;
        var lim = Math.Min(mem.Count, disk.Count);
        while (first < lim && mem[first] == disk[first])
            first++;

        var start = Math.Max(0, first - pad);
        var end = Math.Min(Math.Max(mem.Count, disk.Count), first + maxHunkLines - pad);
        var rows = new List<object>();
        for (var i = start; i < end; i++)
        {
            var m = i < mem.Count ? mem[i] : null;
            var d = i < disk.Count ? disk[i] : null;
            var mark = m == d ? " " : m is null ? "+" : d is null ? "-" : "!";
            rows.Add(new
            {
                line = i + 1,
                mark,
                mem = TrimPeek(m),
                disk = TrimPeek(d)
            });
            if (rows.Count >= maxHunkLines)
                break;
        }

        return new
        {
            schema = "doc_disk_peek/v0",
            path = Path,
            doc_id = DocId,
            disk_changed = changed,
            disk_changed_reason = reason,
            content_same = false,
            missing_on_disk = false,
            dirty = Dirty,
            pulse = Dirty
                ? "DIRTY+DISK — memory ≠ disk (reload loses buffer edits)"
                : "DISK CHANGED — memory ≠ disk",
            first_diff_line = first + 1,
            mem_lines = mem.Count,
            disk_lines = disk.Count,
            disk_mtime_utc = DiskMtimeUtc,
            disk_now_utc = diskNow,
            sample = rows,
            hint = "go=reload (take disk) | go=keep_disk (keep memory). Dirty+drift: reload drops buffer edits."
        };
    }

    static string? TrimPeek(string? s)
    {
        if (s is null)
            return null;
        return s.Length <= 160 ? s : s[..157] + "...";
    }

    public object ToReadResult(int? startLine, int? endLine)
    {
        if (startLine is null && endLine is null)
        {
            return new
            {
                schema = "doc_read/v0",
                meta = ToMeta(),
                text = Text
            };
        }

        var lines = SplitLines(Text);
        var from = Math.Max(1, startLine ?? 1);
        var to = Math.Min(lines.Count, endLine ?? lines.Count);
        if (from > to)
            throw new ArgumentException("start_line > end_line.");
        var slice = new StringBuilder();
        for (var i = from; i <= to; i++)
        {
            if (i > from) slice.Append('\n');
            slice.Append(lines[i - 1]);
        }

        return new
        {
            schema = "doc_read/v0",
            meta = ToMeta(),
            start_line = from,
            end_line = to,
            text = slice.ToString()
        };
    }

    static int CountLines(string text)
    {
        if (text.Length == 0) return 1;
        var n = 1;
        foreach (var ch in text)
        {
            if (ch == '\n') n++;
        }

        return n;
    }

    static List<string> SplitLines(string text)
    {
        var list = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            var len = i - start;
            if (len > 0 && text[i - 1] == '\r') len--;
            list.Add(text.Substring(start, len));
            start = i + 1;
        }

        list.Add(text[start..].TrimEnd('\r'));
        return list;
    }
}
