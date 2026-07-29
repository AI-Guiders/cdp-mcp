#nullable enable

namespace CdpMcp;

/// <summary>
/// Watches disk-LATEST.json for human Save; reloads open agent buffers (shared dirty glass).
/// Ignores origin=agent (self Instant Save already clean in buffer).
/// </summary>
internal sealed class DocumentDiskSyncWatcher : IDisposable
{
    readonly DocumentBufferStore _store;
    readonly FileSystemWatcher _watcher;
    readonly object _gate = new();
    DateTimeOffset _lastStamp = DateTimeOffset.MinValue;
    string? _lastPath;
    bool _disposed;

    DocumentDiskSyncWatcher(DocumentBufferStore store, string stateRoot)
    {
        _store = store;
        Directory.CreateDirectory(stateRoot);
        _watcher = new FileSystemWatcher(stateRoot)
        {
            Filter = "disk-LATEST.json",
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnFsEvent;
        _watcher.Created += OnFsEvent;
        _watcher.Renamed += OnFsEvent;
        TryApplyFromDisk(force: true);
    }

    public static DocumentDiskSyncWatcher Start(DocumentBufferStore store) =>
        new(store, DocumentDiskSyncLatch.StateRoot);

    void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        // MCP host is not Avalonia — apply on thread-pool; ReloadFromDisk is gated.
        _ = Task.Run(() =>
        {
            Thread.Sleep(30);
            TryApplyFromDisk(force: false);
        });
    }

    void TryApplyFromDisk(bool force)
    {
        if (_disposed)
            return;

        var doc = DocumentDiskSyncLatch.TryRead();
        if (doc is null)
            return;
        if (!string.Equals(doc.Origin, DocumentDiskSyncLatch.OriginHuman, StringComparison.OrdinalIgnoreCase))
            return;
        if (!File.Exists(doc.Path))
            return;

        lock (_gate)
        {
            if (!force
                && doc.StampedUtc <= _lastStamp
                && string.Equals(doc.Path, _lastPath, StringComparison.OrdinalIgnoreCase))
                return;
            _lastStamp = doc.StampedUtc;
            _lastPath = doc.Path;
        }

        try
        {
            // Only open buffers — Instant Save sync without inventing tabs/buffers.
            if (!_store.TryGet(doc.Path, out _))
                return;
            _store.ReloadFromDisk(doc.Path);
        }
        catch
        {
            /* best-effort */
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnFsEvent;
        _watcher.Created -= OnFsEvent;
        _watcher.Renamed -= OnFsEvent;
        _watcher.Dispose();
    }
}
