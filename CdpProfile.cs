#nullable enable
using System.Security.Cryptography;
using System.Text;

namespace CdpMcp;

/// <summary>
/// Habitat state isolation (ADR 0199).
/// Priority: <c>CDP_PROFILE</c> env override → MCP client roots → session scm/project → legacy flat default.
/// </summary>
internal static class CdpProfile
{
    static readonly object Gate = new();
    static readonly string EnvNameRaw = Normalize(Environment.GetEnvironmentVariable("CDP_PROFILE"));
    static string _stateRoot = ResolveEnvOrDefault(EnvNameRaw);
    static string _kind = EnvNameRaw.Equals("default", StringComparison.OrdinalIgnoreCase) ? "default" : "env_profile";
    static string? _workspaceLabel;
    static string[] _clientRoots = [];
    static string? _sessionRoot;
    static Action? _onStateRootChanged;

    /// <summary>Env profile name (<c>default</c> when unset).</summary>
    public static string Name => EnvNameRaw;

    public static bool IsDefault =>
        _kind.Equals("default", StringComparison.OrdinalIgnoreCase);

    /// <summary>default | env_profile | client_roots | session</summary>
    public static string Kind
    {
        get { lock (Gate) return _kind; }
    }

    public static string StateRoot
    {
        get { lock (Gate) return _stateRoot; }
    }

    public static string? WorkspaceLabel
    {
        get { lock (Gate) return _workspaceLabel; }
    }

    public static IReadOnlyList<string> ClientRoots
    {
        get { lock (Gate) return _clientRoots; }
    }

    public static void OnStateRootChanged(Action handler) => _onStateRootChanged = handler;

    public static object Snapshot() => new
    {
        kind = Kind,
        state_root = StateRoot,
        env_profile = Name,
        workspace = WorkspaceLabel,
        client_roots = ClientRoots,
        session_root = _sessionRoot,
        adr = "0199"
    };

    /// <summary>Bind MCP client workspace roots. Returns true when StateRoot changed.</summary>
    public static bool ApplyClientRoots(IEnumerable<string?>? urisOrPaths)
    {
        var paths = NormalizePaths(urisOrPaths);
        lock (Gate)
        {
            if (!EnvNameRaw.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                // Explicit env wins — still record roots for diagnostics.
                _clientRoots = paths;
                return false;
            }

            if (paths.Length == 0)
            {
                _clientRoots = [];
                return RebindUnlocked(sessionFallback: true);
            }

            _clientRoots = paths;
            var key = HashKey(paths);
            var label = paths[0];
            var next = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "cdp-mcp", "ws", key);
            return SetRootUnlocked(next, "client_roots", label);
        }
    }

    /// <summary>Fallback when client roots missing — use open project/scm.</summary>
    public static bool ApplySessionWorkspace(string? projectOrScmRoot)
    {
        lock (Gate)
        {
            _sessionRoot = string.IsNullOrWhiteSpace(projectOrScmRoot)
                ? null
                : Path.GetFullPath(projectOrScmRoot.Trim());

            if (!EnvNameRaw.Equals("default", StringComparison.OrdinalIgnoreCase))
                return false;
            if (_clientRoots.Length > 0)
                return false;

            return RebindUnlocked(sessionFallback: true);
        }
    }

    static bool RebindUnlocked(bool sessionFallback)
    {
        if (!EnvNameRaw.Equals("default", StringComparison.OrdinalIgnoreCase))
            return SetRootUnlocked(ResolveEnvOrDefault(EnvNameRaw), "env_profile", EnvNameRaw);

        if (_clientRoots.Length > 0)
        {
            var key = HashKey(_clientRoots);
            var next = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "cdp-mcp", "ws", key);
            return SetRootUnlocked(next, "client_roots", _clientRoots[0]);
        }

        if (sessionFallback && _sessionRoot is { Length: > 0 })
        {
            var key = HashKey([_sessionRoot]);
            var next = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "cdp-mcp", "ws", key);
            return SetRootUnlocked(next, "session", _sessionRoot);
        }

        return SetRootUnlocked(ResolveEnvOrDefault("default"), "default", null);
    }

    static bool SetRootUnlocked(string next, string kind, string? label)
    {
        var changed = !string.Equals(_stateRoot, next, StringComparison.OrdinalIgnoreCase)
                      || !string.Equals(_kind, kind, StringComparison.Ordinal);
        _stateRoot = next;
        _kind = kind;
        _workspaceLabel = label;
        if (changed)
        {
            try { Directory.CreateDirectory(next); } catch { /* best-effort */ }
            _onStateRootChanged?.Invoke();
        }

        return changed;
    }

    static string ResolveEnvOrDefault(string name)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (name.Equals("default", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(local, "cdp-mcp");
        return Path.Combine(local, "cdp-mcp", "profiles", name);
    }

    static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "default";
        var s = raw.Trim().ToLowerInvariant();
        Span<char> buf = stackalloc char[s.Length];
        var n = 0;
        foreach (var c in s)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
                buf[n++] = c;
        }

        return n == 0 ? "default" : new string(buf[..n]);
    }

    internal static string[] NormalizePaths(IEnumerable<string?>? urisOrPaths)
    {
        if (urisOrPaths is null) return [];
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in urisOrPaths)
        {
            var path = UriToPath(raw);
            if (path is null) continue;
            try { set.Add(Path.GetFullPath(path)); }
            catch { /* skip */ }
        }

        return set.ToArray();
    }

    internal static string? UriToPath(string? uriOrPath)
    {
        if (string.IsNullOrWhiteSpace(uriOrPath)) return null;
        var s = uriOrPath.Trim();
        if (s.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(s, UriKind.Absolute, out var uri) && uri.IsFile)
                return uri.LocalPath;
            // file:///D:/foo or file:/D:/foo
            var stripped = s["file:".Length..].TrimStart('/');
            if (stripped.Length >= 2 && stripped[1] == ':')
                return stripped.Replace('/', Path.DirectorySeparatorChar);
            return null;
        }

        return s;
    }

    internal static string HashKey(IReadOnlyList<string> paths)
    {
        var joined = string.Join('|', paths.Select(p => p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant()));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(hash.AsSpan(0, 6)).ToLowerInvariant(); // 12 hex chars
    }
}
