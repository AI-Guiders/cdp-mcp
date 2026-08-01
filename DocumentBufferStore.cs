using System.Text;

namespace CdpMcp;

/// <summary>
/// Session document buffers — agent edits go here; flush + diagnose ≈ almost-online LSP.
/// Disk mutates go through <see cref="PathMutateGate"/> + <see cref="AtomicTextFile"/> so parallel
/// agent tool calls stay comfortable: different paths concurrent, same path serialized.
/// </summary>
internal sealed partial class DocumentBufferStore
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

    /// <summary>
    /// On project switch: drop clean buffers outside the new root; keep dirty foreign ones
    /// (agent must flush/close) so work is not silently lost.
    /// </summary>
    public BufferParkResult ParkOutsideProject(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            return BufferParkResult.Empty;

        string root;
        try
        {
            root = Path.GetFullPath(projectRoot);
        }
        catch
        {
            return BufferParkResult.Empty;
        }

        var keptDirty = new List<string>();
        var closed = 0;
        foreach (var buf in _byPath.Values.ToList())
        {
            if (IsUnderProjectRoot(buf.Path, root))
                continue;
            if (buf.Dirty)
            {
                keptDirty.Add(buf.Path);
                continue;
            }

            _byPath.Remove(buf.Path);
            EditorComfort.ClearStack(buf.Path);
            closed++;
        }

        return new BufferParkResult(closed, keptDirty);
    }

    public static bool IsUnderProjectRoot(string path, string projectRoot)
    {
        string full;
        string root;
        try
        {
            full = Path.GetFullPath(path);
            root = Path.GetFullPath(projectRoot);
        }
        catch
        {
            return false;
        }

        var rel = Path.GetRelativePath(root, full);
        if (string.IsNullOrEmpty(rel) || rel is ".")
            return true;
        return !Path.IsPathRooted(rel) && !rel.StartsWith("..", StringComparison.Ordinal);
    }

    public readonly record struct BufferParkResult(int ClosedClean, IReadOnlyList<string> KeptDirty)
    {
        public static BufferParkResult Empty { get; } = new(0, Array.Empty<string>());

        public string? Note =>
            KeptDirty.Count > 0
                ? $"Parked {ClosedClean} clean foreign buffer(s); kept {KeptDirty.Count} dirty outside project — flush/close before discard."
                : ClosedClean > 0
                    ? $"Parked {ClosedClean} clean foreign buffer(s) outside new project_root."
                    : null;
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
        DocumentDiskSyncLatch.Publish(buf.Path, DocumentDiskSyncLatch.OriginAgent);
    }

        public object Scene()
    {
        var docs = _byPath.Values
            .OrderBy(b => b.DocId, StringComparer.Ordinal)
            .Select(b => b.ToMeta())
            .ToArray();
        var drift = _byPath.Values.Count(b => b.ProbeMaterialDiskChanged(out _, out _));
        return new
        {
            schema = "doc_scene/v0",
            count = _byPath.Count,
            dirty_count = _byPath.Values.Count(b => b.Dirty),
            disk_changed_count = drift,
            docs,
            habitat =
                "PathMutateGate covers cdp_buffer only — Cursor host Write/Read bypass the desk. Prefer buffer open/edit/find over host Write/Read.",
            hint =
                "Mutate SSOT via cdp_buffer. Large files: edit_op=anchor|replace / go=scope — avoid thick set_text."
        };
    }

}
