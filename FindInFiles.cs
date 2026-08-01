using System.Diagnostics;
using System.Text;
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
        {
            return JsonSerializer.Serialize(new
            {
                schema = EditorComfort.Schema,
                ok = false,
                op = all ? "find_all" : "find",
                scope = scopeWire,
                error = "query_required",
                hint = "query= + scope=project|external. regex=true = Use Regular Expressions."
            }, Pretty);
        }

        var multiPaths = OptPaths(args);
        string searchRoot;
        string cwd;
        if (multiPaths is { Count: > 0 })
        {
            searchRoot = multiPaths[0];
            cwd = Directory.Exists(searchRoot)
                ? searchRoot
                : (Path.GetDirectoryName(searchRoot)
                   ?? session.ProjectRoot
                   ?? Environment.CurrentDirectory);
            if (!external && session.ProjectRoot is { Length: > 0 } && Directory.Exists(session.ProjectRoot))
                cwd = session.ProjectRoot!;
        }
        else if (!TryResolveSearchRoot(session, args, external, out searchRoot, out cwd, out var rootError, out var rootHint))
        {
            return JsonSerializer.Serialize(new
            {
                schema = EditorComfort.Schema,
                ok = false,
                op = all ? "find_all" : "find",
                scope = scopeWire,
                error = rootError,
                hint = rootHint
            }, Pretty);
        }

        var rg = ResolveRg();
        if (rg is null)
        {
            return JsonSerializer.Serialize(new
            {
                schema = EditorComfort.Schema,
                ok = false,
                op = all ? "find_all" : "find",
                scope = scopeWire,
                error = "rg_not_found",
                hint = "Install ripgrep on PATH, or set env CDP_RG to rg.exe"
            }, Pretty);
        }

        var regex = BoolOr(args, "regex", false);
        var ignoreCase = BoolOr(args, "ignore_case", true);
        var max = Math.Clamp(
            IntOr(args, "max", all ? EditorComfort.MaxFindHits : DefaultMax),
            1,
            HardMax);

        var glob = Opt(args, "glob") ?? Opt(args, "g");
        var volumeProbe = multiPaths is { Count: > 0 } ? multiPaths[0] : searchRoot;
        if (external && IsVolumeRoot(volumeProbe) && glob is not { Length: > 0 } && multiPaths is not { Count: > 1 })
        {
            return JsonSerializer.Serialize(new
            {
                schema = EditorComfort.Schema,
                ok = false,
                op = all ? "find_all" : "find",
                scope = scopeWire,
                error = "glob_required_for_volume_root",
                path = volumeProbe,
                hint = "Volume root search needs glob= (e.g. *.cs) or a narrower path=."
            }, Pretty);
        }

        var argv = new List<string>
        {
            "--json",
            "--color", "never",
            // Per-file cap (rg); global cap applied while parsing.
            "--max-count", "5"
        };
        if (ignoreCase)
            argv.Add("-i");
        if (!regex)
            argv.Add("-F");

        foreach (var g in SkipGlobs)
            argv.AddRange(["--glob", g]);

        if (glob is { Length: > 0 })
            argv.AddRange(["--glob", glob]);

        var type = Opt(args, "type") ?? Opt(args, "filetype");
        if (type is { Length: > 0 })
            argv.AddRange(["--type", type]);

        argv.Add("--");
        argv.Add(query!);
        if (multiPaths is { Count: > 0 })
            argv.AddRange(multiPaths);
        else
            argv.Add(searchRoot);

        var timeout = external ? ExternalTimeoutMs : TimeoutMs;
        if (!TryRunRg(rg, argv, cwd, timeout, out var stdout, out var stderr, out var exit, out var runError))
        {
            return JsonSerializer.Serialize(new
            {
                schema = EditorComfort.Schema,
                ok = false,
                op = all ? "find_all" : "find",
                scope = scopeWire,
                error = "rg_failed",
                detail = runError,
                hint = "Check CDP_RG / PATH and query (regex syntax)."
            }, Pretty);
        }

        // rg: 0 = matches, 1 = no matches, 2 = error
        if (exit >= 2)
        {
            return JsonSerializer.Serialize(new
            {
                schema = EditorComfort.Schema,
                ok = false,
                op = all ? "find_all" : "find",
                scope = scopeWire,
                error = "rg_exit",
                exit_code = exit,
                stderr = Trim(stderr, 800),
                hint = "Bad regex or path? Try regex=false or narrower glob="
            }, Pretty);
        }

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

        return JsonSerializer.Serialize(new
        {
            schema = EditorComfort.Schema,
            ok = true,
            op = all ? "find_all" : "find",
            scope = scopeWire,
            path = searchRoot,
            query,
            regex,
            ignore_case = ignoreCase,
            engine = "rg",
            rg_path = rg,
            count = hits.Count,
            truncated = hits.Count >= max,
            hits = hits.Select(h => new
            {
                anchor = h.Anchor,
                path = h.AbsolutePath,
                line = h.Line,
                column = h.Column,
                preview = h.Preview
            }),
            land,
            next = hits.Count > 0
                ? (object[])
                [
                    new { go = "complete", label = "Completions at hit", why = "line/column from hits[0]" },
                    new { go = "signature_help", label = "Signature help", why = "near hit" },
                    new { go = "scope", label = "Sniper from land", why = $"from={hits[0].Anchor}" },
                    new { go = "edit_draft", label = "Edit here", why = "land open+peeked" },
                    new { go = "find_all", label = "More hits", why = $"same query + scope={scopeWire}" }
                ]
                : (object[])
                [
                    new { go = "find", label = "Retry", why = $"regex= / glob= / scope={scopeWire} / path=" }
                ],
            hint =
                "VS Find in Files. scope=project|files|external → rg → anchors. " +
                "external requires path= (any disk tree; no cdp_open). " +
                "regex=true = Use Regular Expressions. find = first; find_all = capped list."
        }, Pretty);
    }
}
