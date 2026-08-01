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
