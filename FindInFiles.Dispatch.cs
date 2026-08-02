#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>Dispatch helpers for Find in Files (method_lines peel).</summary>
internal static partial class FindInFiles
{
    static string OpName(bool all) => all ? "find_all" : "find";

    static string FailJson(bool all, string scope, string error, string hint) =>
        JsonSerializer.Serialize(new
        {
            schema = EditorComfort.Schema,
            ok = false,
            op = OpName(all),
            scope,
            error,
            hint
        }, Pretty);

    static string FailDetailJson(bool all, string scope, string error, string? detail, string hint) =>
        JsonSerializer.Serialize(new
        {
            schema = EditorComfort.Schema,
            ok = false,
            op = OpName(all),
            scope,
            error,
            detail,
            hint
        }, Pretty);

    static string FailExitJson(bool all, string scope, int exit, string stderr, string hint) =>
        JsonSerializer.Serialize(new
        {
            schema = EditorComfort.Schema,
            ok = false,
            op = OpName(all),
            scope,
            error = "rg_exit",
            exit_code = exit,
            stderr,
            hint
        }, Pretty);

    static string FailPathJson(bool all, string scope, string error, string path, string hint) =>
        JsonSerializer.Serialize(new
        {
            schema = EditorComfort.Schema,
            ok = false,
            op = OpName(all),
            scope,
            error,
            path,
            hint
        }, Pretty);

    static bool TryBindRoots(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        bool external,
        List<string>? multiPaths,
        out string searchRoot,
        out string cwd,
        out string? error,
        out string? hint)
    {
        searchRoot = "";
        cwd = "";
        error = null;
        hint = null;
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
            return true;
        }

        if (TryResolveSearchRoot(session, args, external, out searchRoot, out cwd, out var rootError, out var rootHint))
            return true;

        error = rootError;
        hint = rootHint;
        return false;
    }

    static List<string> BuildRgArgv(
        string query,
        bool regex,
        bool ignoreCase,
        string? glob,
        string? type,
        string searchRoot,
        List<string>? multiPaths)
    {
        var argv = new List<string>
        {
            "--json",
            "--color", "never",
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

        if (type is { Length: > 0 })
            argv.AddRange(["--type", type]);

        argv.Add("--");
        argv.Add(query);
        if (multiPaths is { Count: > 0 })
            argv.AddRange(multiPaths);
        else
            argv.Add(searchRoot);
        return argv;
    }

    static string OkHitsJson(
        bool all,
        string scopeWire,
        string searchRoot,
        string query,
        bool regex,
        bool ignoreCase,
        string rg,
        int max,
        IReadOnlyList<Hit> hits,
        object? land)
    {
        return JsonSerializer.Serialize(new
        {
            schema = EditorComfort.Schema,
            ok = true,
            op = OpName(all),
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
