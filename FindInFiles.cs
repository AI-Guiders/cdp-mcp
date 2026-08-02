using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// VS Find in Files — corpus text search via ripgrep, results as anchors.
/// Called from EditorComfort find when scope=project|files|external|… (regex= is Use Regular Expressions).
/// </summary>
internal static partial class FindInFiles
{
    public const int DefaultMax = 40;
    public const int HardMax = 200;
    public const int TimeoutMs = 20_000;
    public const int ExternalTimeoutMs = 60_000;

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly HashSet<string> SkipGlobs = new(StringComparer.OrdinalIgnoreCase)
    {
        "!**/bin/**", "!**/obj/**", "!**/.git/**", "!**/.vs/**",
        "!**/node_modules/**", "!**/packages/**", "!**/TestResults/**",
        "!**/publish/**", "!**/publish-release/**", "!**/dist/**"
    };

    public static bool IsExternalScope(string? scope)
    {
        var s = (scope ?? "").Trim().ToLowerInvariant();
        return s is "external" or "disk" or "system" or "fs" or "anywhere";
    }

    public static bool IsFilesScope(string? scope)
    {
        var s = (scope ?? "").Trim().ToLowerInvariant();
        return s is "project" or "files" or "solution" or "workspace" or "all" or "repo"
            || IsExternalScope(s);
    }

    /// <summary>Optional multi-root / file list (dirty, buffers, roots[]). When set, overrides single searchRoot as rg paths.</summary>
    public static List<string>? OptPaths(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!args.TryGetValue("paths", out var el) && !args.TryGetValue("roots", out el))
            return null;

        var list = new List<string>();
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                    list.Add(Path.GetFullPath(s.Trim()));
            }
        }
        else if (el.ValueKind == JsonValueKind.String && el.GetString() is { Length: > 0 } csv)
        {
            foreach (var part in csv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                list.Add(Path.GetFullPath(part));
        }

        return list.Count > 0 ? list : null;
    }

    public static string Dispatch(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        bool all)
    {
        var scopeRaw = Opt(args, "scope") ?? Opt(args, "in") ?? "project";
        var external = IsExternalScope(scopeRaw);
        var scopeWire = external ? "external" : "project";

        var query = Opt(args, "query") ?? Opt(args, "text") ?? Opt(args, "pattern");
        if (string.IsNullOrEmpty(query))
            return FailJson(all, scopeWire, "query_required",
                "query= + scope=project|external. regex=true = Use Regular Expressions.");

        var multiPaths = OptPaths(args);
        if (!TryBindRoots(session, args, external, multiPaths, out var searchRoot, out var cwd, out var rootError, out var rootHint))
            return FailJson(all, scopeWire, rootError!, rootHint!);

        var rg = ResolveRg();
        if (rg is null)
            return FailJson(all, scopeWire, "rg_not_found",
                "Install ripgrep on PATH, or set env CDP_RG to rg.exe");

        var regex = BoolOr(args, "regex", false);
        var ignoreCase = BoolOr(args, "ignore_case", true);
        var max = Math.Clamp(
            IntOr(args, "max", all ? EditorComfort.MaxFindHits : DefaultMax),
            1,
            HardMax);

        var glob = Opt(args, "glob") ?? Opt(args, "g");
        var volumeProbe = multiPaths is { Count: > 0 } ? multiPaths[0] : searchRoot;
        if (external && IsVolumeRoot(volumeProbe) && glob is not { Length: > 0 } && multiPaths is not { Count: > 1 })
            return FailPathJson(all, scopeWire, "glob_required_for_volume_root", volumeProbe,
                "Volume root search needs glob= (e.g. *.cs) or a narrower path=.");

        var argv = BuildRgArgv(query!, regex, ignoreCase, glob, Opt(args, "type") ?? Opt(args, "filetype"),
            searchRoot, multiPaths);
        var timeout = external ? ExternalTimeoutMs : TimeoutMs;
        if (!TryRunRg(rg, argv, cwd, timeout, out var stdout, out var stderr, out var exit, out var runError))
            return FailDetailJson(all, scopeWire, "rg_failed", runError,
                "Check CDP_RG / PATH and query (regex syntax).");

        if (exit >= 2)
            return FailExitJson(all, scopeWire, exit, Trim(stderr, 800),
                "Bad regex or path? Try regex=false or narrower glob=");

        var hits = ParseJsonHits(session, stdout, max);
        if (!all && hits.Count > 1)
            hits = hits.Take(1).ToList();

        object? land = null;
        if (hits.Count > 0)
        {
            EditorComfort.PushLocus(session, hits[0].Anchor);
            if (BoolOr(args, "peek", true))
                land = TryLand(store, session, hits[0]);
        }

        return OkHitsJson(all, scopeWire, searchRoot, query!, regex, ignoreCase, rg, max, hits, land);
    }
}
