using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// VS-inspired code clone detection (exact / strong). Surfaces anchors, never paths.
/// </summary>
internal static partial class CodeClones
{
    public const string Schema = "code_clones/v0";
    public const int DefaultMinStatementsProject = 10;
    public const int DefaultMinStatementsLocal = 3;
    public const int MaxFilesDefault = 200;
    public const int MaxGroupsDefault = 40;
    public const int MaxInstancesPerGroup = 12;

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly HashSet<string> SkipDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", "node_modules", "packages", "TestResults"
    };

    sealed record Fragment(
        string AbsolutePath,
        string FileLabel,
        string? Member,
        int LineStart,
        int LineEnd,
        string ExactKey,
        string StrongKey,
        int StatementCount);

    public static string Run(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var scope = (OptString(args, "scope") ?? "file").Trim().ToLowerInvariant();
        if (scope is "select")
            scope = "selection";

        var minDefault = scope is "project" or "solution"
            ? DefaultMinStatementsProject
            : DefaultMinStatementsLocal;
        var minStatements = Math.Clamp(IntOr(args, "min_statements", minDefault), 2, 80);
        var maxFiles = Math.Clamp(IntOr(args, "max_files", MaxFilesDefault), 1, 2000);
        var maxGroups = Math.Clamp(IntOr(args, "max_groups", MaxGroupsDefault), 1, 200);

        string? seedError = null;
        Fragment? seed = null;
        var seedWire = OptString(args, "anchor") ?? OptString(args, "from") ?? OptString(args, "at");
        if (seedWire is { Length: > 0 } || scope is "selection" or "method")
        {
            seed = TryBuildSeed(store, session, args, seedWire, minStatements, out seedError);
            if (seed is null && seedError is not null)
            {
                return JsonSerializer.Serialize(new
                {
                    schema = Schema,
                    ok = false,
                    feature = "clones",
                    scope,
                    error = seedError,
                    hint = "Pass anchor=/from= [F:;M:;L:] or path= + start_line=/end_line= for selection/method."
                }, Pretty);
            }
        }

        if (!TryCollectCorpus(store, session, args, scope, seed, maxFiles, out var files, out var corpusError))
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                feature = "clones",
                scope,
                error = corpusError,
                hint = "cdp_open + path= / open buffer; scope=file|method|selection|project|solution"
            }, Pretty);
        }

        var fragments = new List<Fragment>();
        foreach (var (abs, label) in files)
        {
            string text;
            try
            {
                text = ReadText(store, abs);
            }
            catch
            {
                continue;
            }

            fragments.AddRange(ExtractWindows(abs, label, text, minStatements));
        }

        if (seed is not null)
            fragments = FilterMatchingSeed(fragments, seed);

        var groups = GroupClones(fragments, maxGroups);
        var pulse = groups.Count == 0
            ? "clones none"
            : $"clones groups={groups.Count}";

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            feature = "clones",
            scope,
            min_statements = minStatements,
            files_scanned = files.Count,
            fragments = fragments.Count,
            groups = groups.Count,
            pulse,
            seed = seed is null
                ? null
                : new
                {
                    anchor = WireOf(seed),
                    member = seed.Member,
                    statements = seed.StatementCount
                },
            clone_groups = groups,
            next = BuildNext(groups),
            hint =
                "Anchors only. exact = identical tokens; strong = same structure / renamed ids. " +
                "go=peek / edit_draft on an anchor; scope= shrinks corpus."
        }, Pretty);
    }

    static List<object> GroupClones(List<Fragment> fragments, int maxGroups)
    {
        // Prefer exact groups; then strong that are not already covered as exact pairs.
        var groups = new List<object>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        void Emit(string strength, IEnumerable<IGrouping<string, Fragment>> buckets)
        {
            foreach (var bucket in buckets
                         .Where(g => g.Count() >= 2)
                         .OrderByDescending(g => g.Count())
                         .ThenByDescending(g => g.First().StatementCount))
            {
                if (groups.Count >= maxGroups)
                    return;

                var instances = bucket
                    .OrderBy(f => f.FileLabel, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => f.LineStart)
                    .Take(MaxInstancesPerGroup)
                    .Select(f =>
                    {
                        var key = $"{f.AbsolutePath}|{f.LineStart}|{f.LineEnd}";
                        claimed.Add(key);
                        return new
                        {
                            anchor = WireOf(f),
                            member = f.Member,
                            statements = f.StatementCount
                        };
                    })
                    .ToList();

                if (instances.Count < 2)
                    continue;

                groups.Add(new
                {
                    strength,
                    count = instances.Count,
                    statements = bucket.First().StatementCount,
                    instances
                });
            }
        }

        Emit("exact", fragments.GroupBy(f => f.ExactKey, StringComparer.Ordinal));

        // Strong: skip fragments already paired in an exact group of size≥2.
        var remaining = fragments
            .Where(f => !claimed.Contains($"{f.AbsolutePath}|{f.LineStart}|{f.LineEnd}"))
            .ToList();
        Emit("strong", remaining.GroupBy(f => f.StrongKey, StringComparer.Ordinal));

        return groups;
    }

    static List<Fragment> FilterMatchingSeed(List<Fragment> all, Fragment seed)
    {
        var hits = all
            .Where(f =>
                f.ExactKey == seed.ExactKey
                || f.StrongKey == seed.StrongKey)
            .Where(f => !(f.AbsolutePath == seed.AbsolutePath
                          && f.LineStart == seed.LineStart
                          && f.LineEnd == seed.LineEnd))
            .ToList();

        // Always include the seed so the group is visible.
        hits.Insert(0, seed);
        return hits;
    }

    static object[] BuildNext(List<object> groups)
    {
        if (groups.Count == 0)
        {
            return
            [
                new { go = "analysis_scene", label = "Widen scope", why = "scope=project|solution" },
                new { go = "analysis_scene", label = "Lower min_statements", why = "go_args.min_statements=3" }
            ];
        }

        return
        [
            new { go = "peek", label = "Peek a clone", why = "go_args.wire= from instances[].anchor" },
            new { go = "edit_draft", label = "Draft extract/refactor", why = "consolidate duplicate anchors" },
            new { go = "scope", label = "Sniper corridor", why = "from=/till= around a clone" }
        ];
    }

    static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    static int IntOr(IReadOnlyDictionary<string, JsonElement> args, string key, int defaultValue)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.Number)
            return defaultValue;
        return el.TryGetInt32(out var n) ? n : defaultValue;
    }

    static int? IntOrNullable(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.Number)
            return null;
        return el.TryGetInt32(out var n) ? n : null;
    }
}
