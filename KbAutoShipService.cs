namespace CdpMcp;

/// <summary>
/// CDP-ADR-0224: background FileSystemWatcher on KB git roots; debounced commit+push.
/// Static root ref in <see cref="CdpServiceHost"/> keeps timers alive (LineWakePoller pattern).
/// </summary>
internal sealed class KbAutoShipService : IDisposable
{
    readonly KbAutoShipOptions _options;
    readonly KbAutoShipEngine _engine;
    readonly object _gate = new();
    readonly Dictionary<string, RootWatch> _roots = new(StringComparer.OrdinalIgnoreCase);
    bool _started;

    internal KbAutoShipService(KbAutoShipOptions options, KbAutoShipEngine? engine = null)
    {
        _options = options;
        _engine = engine ?? new KbAutoShipEngine();
    }

    public static KbAutoShipService? TryCreate(CdpSettings settings, KbAutoShipEngine? engine = null)
    {
        var options = KbAutoShipOptions.FromSettings(settings);
        if (!options.Enabled || options.Roots.Count == 0)
            return null;
        return new KbAutoShipService(options, engine);
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started)
                return;
            _started = true;

            foreach (var root in _options.Roots)
            {
                if (!Directory.Exists(root))
                    continue;
                var watch = new RootWatch(root, _options, _engine, _gate);
                watch.Start();
                _roots[root] = watch;
            }

            if (_roots.Count > 0)
            {
                Console.Error.WriteLine(
                    $"[KbAutoShip] watching {_roots.Count} root(s), debounce={_options.DebounceMs}ms, branch={_options.Branch}");
            }
        }
    }

    /// <summary>Test seam — simulate FS activity without FileSystemWatcher.</summary>
    internal void NotifyChange(string root)
    {
        lock (_gate)
        {
            if (_roots.TryGetValue(root, out var watch))
                watch.ScheduleShip();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var watch in _roots.Values)
                watch.Dispose();
            _roots.Clear();
            _started = false;
        }
    }

    sealed class RootWatch : IDisposable
    {
        readonly string _root;
        readonly KbAutoShipOptions _options;
        readonly KbAutoShipEngine _engine;
        readonly object _gate;
        FileSystemWatcher? _watcher;
        Timer? _debounce;
        int _shipping;

        public RootWatch(string root, KbAutoShipOptions options, KbAutoShipEngine engine, object gate)
        {
            _root = root;
            _options = options;
            _engine = engine;
            _gate = gate;
        }

        public void Start()
        {
            _watcher = new FileSystemWatcher(_root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
            };
            _watcher.Changed += OnFsEvent;
            _watcher.Created += OnFsEvent;
            _watcher.Deleted += OnFsEvent;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += (_, e) =>
                Console.Error.WriteLine($"[KbAutoShip] watcher error ({_root}): {e.GetException().Message}");
            _watcher.EnableRaisingEvents = true;
        }

        void OnFsEvent(object sender, FileSystemEventArgs e)
        {
            if (ShouldIgnore(e.FullPath))
                return;
            ScheduleShip();
        }

        void OnRenamed(object sender, RenamedEventArgs e)
        {
            if (ShouldIgnore(e.FullPath) && ShouldIgnore(e.OldFullPath))
                return;
            ScheduleShip();
        }

        bool ShouldIgnore(string fullPath)
        {
            try
            {
                var rel = Path.GetRelativePath(_root, fullPath);
                if (rel.Equals(".git", StringComparison.OrdinalIgnoreCase)
                    || rel.StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || rel.StartsWith(".git" + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }

            return false;
        }

        public void ScheduleShip()
        {
            lock (_gate)
            {
                _debounce?.Dispose();
                _debounce = new Timer(
                    _ => FireShip(),
                    null,
                    _options.DebounceMs,
                    Timeout.Infinite);
            }
        }

        void FireShip()
        {
            if (Interlocked.CompareExchange(ref _shipping, 1, 0) != 0)
                return;

            try
            {
                _ = _engine.TryShip(_root, _options);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[KbAutoShip] ship exception ({_root}): {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _shipping, 0);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _debounce?.Dispose();
                _debounce = null;
            }

            if (_watcher is null)
                return;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }
}
