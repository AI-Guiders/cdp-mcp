using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using TypescriptLang;
using Tool = ModelContextProtocol.Protocol.Tool;

namespace CdpMcp;

/// <summary>
/// Bare IDE verbs + <c>cdp_open</c> helpers. Harness routes by <see cref="SessionContext.Language"/>.
/// </summary>
internal static class IdeLanguageTools
{
    private static readonly HashSet<string> BareVerbs = new(StringComparer.Ordinal)
    {
        "go_to_definition",
        "find_usages",
        "get_document_symbols",
        "get_symbol_at_position",
        "get_diagnostics",
        "resolve_project_root",
        "get_workspace_navigation_context"
    };

    private static readonly object TsGate = new();
    private static TypescriptLanguageClient? _ts;
    private static string? _tsOpenedRoot;
    private static LanguageRegistry _langs = LanguageRegistry.Default;

    public static void Configure(LanguageRegistry languages) => _langs = languages;

    public static LanguageRegistry Languages => _langs;

    public static bool IsBareVerb(string name) => BareVerbs.Contains(name);

    public static IEnumerable<Tool> BuildBareVerbTools()
    {
        yield return Tool("go_to_definition",
            "IDE: go to definition. Harness routes LSP by session language (after cdp_open). 1-based line/column.",
            PositionalSchema());
        yield return Tool("find_usages",
            "IDE: find references/usages. Harness routes by session language.",
            PositionalSchema());
        yield return Tool("get_document_symbols",
            "IDE: outline symbols in a file.",
            new
            {
                type = "object",
                properties = new
                {
                    file_path = new { type = "string" },
                    language = new { type = "string", description = "optional override from [languages] config" }
                },
                required = new[] { "file_path" }
            });
        yield return Tool("get_symbol_at_position",
            "IDE: symbol / quick-info at position.",
            PositionalSchema());
        yield return Tool("get_diagnostics",
            "IDE: diagnostics for a file (prefer over host ReadLints when language is open).",
            new
            {
                type = "object",
                properties = new
                {
                    file_path = new { type = "string" },
                    language = new { type = "string", description = "optional override" }
                },
                required = new[] { "file_path" }
            });
        yield return Tool("resolve_project_root",
            "Resolve project root / language markers from a path (or return session project after cdp_open).",
            new
            {
                type = "object",
                properties = new
                {
                    path = new { type = "string", description = "optional; omit to echo session project" }
                }
            });
        yield return Tool("get_workspace_navigation_context",
            "IDE: Semantic Map related/subgraph (csharp). Wide strokes: partial/xaml/tests first, then same_directory≤4 (name-affinity), same_namespace≤4, project_peer≤3. Not a usages graph — use find_usages for detail. preset=explore_default excludes project_peer; peers_only = old dump. After cdp_open .sln/.csproj.",
            new
            {
                type = "object",
                properties = new
                {
                    file_path = new { type = "string", description = "Anchor file in the opened solution." },
                    mode = new { type = "string", description = "related | subgraph" },
                    line = new { type = "integer", description = "optional 1-based" },
                    column = new { type = "integer", description = "optional 1-based" },
                    max_related = new { type = "integer" },
                    max_nodes = new { type = "integer" },
                    max_edges = new { type = "integer" },
                    include_kinds = new { type = "array", items = new { type = "string" } },
                    exclude_kinds = new { type = "array", items = new { type = "string" } },
                    preset = new { type = "string" },
                    language = new { type = "string", description = "optional override; csharp-only v0" },
                    solution_or_project_path = new { type = "string", description = "optional; default session after cdp_open" }
                },
                required = new[] { "file_path", "mode" }
            });
    }

    private static object PositionalSchema() => new
    {
        type = "object",
        properties = new
        {
            file_path = new { type = "string" },
            line = new { type = "integer", description = "1-based" },
            column = new { type = "integer", description = "1-based" },
            language = new { type = "string", description = "optional override from [languages] config" },
            solution_or_project_path = new { type = "string", description = "csharp escape; default session after cdp_open" }
        },
        required = new[] { "file_path", "line", "column" }
    };

    private static Tool Tool(string name, string desc, object schema) => new()
    {
        Name = name,
        Description = desc,
        InputSchema = JsonSerializer.SerializeToElement(schema)
    };

    public static string ApplyOpen(SessionContext session, ProjectOpenResult open)
    {
        session.ProjectRoot = open.Root;
        session.ProjectKind = open.Kind;
        session.Language = CdpLanguages.IsAny(open.Language) ? null : open.Language;
        session.SolutionOrProjectPath = open.SolutionOrProjectPath;
        session.TsConfigPath = open.TsConfigPath;
        session.ScmRoot = GitSessionDefaults.TryResolveScmRoot(open.Root);
        session.Phase = CdpPhase.Explore;
        session.Object = CdpObjectKind.Code;
        var recentPath = open.SolutionOrProjectPath ?? open.TsConfigPath ?? open.Anchors.FirstOrDefault() ?? open.Root;
        if (!string.IsNullOrWhiteSpace(recentPath))
            OpenRecentStore.Push(recentPath!, open.Root, open.Kind, open.Language);
        return JsonSerializer.Serialize(new
        {
            root = open.Root,
            kind = open.Kind,
            language = open.Language,
            anchors = open.Anchors,
            solution_or_project_path = open.SolutionOrProjectPath,
            tsconfig_path = open.TsConfigPath,
            scm_root = session.ScmRoot,
            session_phase = CdpEnumParse.ToWire(session.Phase),
            session_object = CdpEnumParse.ToWire(session.Object),
            recent_count = OpenRecentStore.List().Count,
            recent_store = OpenRecentStore.Location
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    public static async Task<string> DispatchBareAsync(
        string name,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        if (name == "resolve_project_root")
        {
            if (!args.TryGetValue("path", out var pEl) || pEl.GetString() is not { Length: > 0 } path)
            {
                return JsonSerializer.Serialize(new
                {
                    session_project_root = session.ProjectRoot,
                    session_kind = session.ProjectKind,
                    session_language = session.Language,
                    solution_or_project_path = session.SolutionOrProjectPath,
                    tsconfig_path = session.TsConfigPath,
                    scm_root = session.ScmRoot
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            var detected = _langs.Detect(path);
            return JsonSerializer.Serialize(new
            {
                root = detected.Root,
                kind = detected.Kind,
                language = detected.Language,
                anchors = detected.Anchors,
                solution_or_project_path = detected.SolutionOrProjectPath,
                tsconfig_path = detected.TsConfigPath
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        var lang = ResolveLanguage(session, args);
        if (name == "get_workspace_navigation_context")
        {
            if (!lang.Equals(CdpLanguages.Csharp, StringComparison.OrdinalIgnoreCase)
                && !CdpLanguages.IsAny(lang))
            {
                throw new ArgumentException(
                    "get_workspace_navigation_context is csharp-only in v0; open a .sln/.csproj scene (or language=csharp).");
            }

            return await DispatchCsharpAsync(name, session, byDomain, args, cancellationToken).ConfigureAwait(false);
        }

        if (lang.Equals(CdpLanguages.Csharp, StringComparison.OrdinalIgnoreCase))
            return await DispatchCsharpAsync(name, session, byDomain, args, cancellationToken).ConfigureAwait(false);
        if (lang.Equals(CdpLanguages.Typescript, StringComparison.OrdinalIgnoreCase))
            return await DispatchTypescriptAsync(name, session, args, cancellationToken).ConfigureAwait(false);

        throw new ArgumentException(
            $"No IDE engine for language '{lang}'. Call cdp_open(path) first, or pass language= from [languages] (csharp|typescript today).");
    }

    private static string ResolveLanguage(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        if (args.TryGetValue("language", out var el)
            && _langs.TryNormalize(el.GetString(), out var overrideLang)
            && !CdpLanguages.IsAny(overrideLang))
            return overrideLang;
        if (!CdpLanguages.IsAny(session.Language))
            return session.Language!;
        return CdpLanguages.Any;
    }

    private static async Task<string> DispatchCsharpAsync(
        string name,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        if (!byDomain.TryGetValue(CdpDomains.Roslyn, out var roslyn))
            throw new InvalidOperationException("Roslyn backend not mounted ([dev.roslyn] enabled=false).");

        var mapped = MapToRoslynArgs(name, session, args);
        var underlying = name switch
        {
            "go_to_definition" => "roslyn_go_to_definition",
            "find_usages" => "roslyn_find_usages",
            "get_document_symbols" => "roslyn_get_document_symbols",
            "get_symbol_at_position" => "roslyn_get_symbol_at_position",
            "get_diagnostics" => "roslyn_get_diagnostics",
            "get_workspace_navigation_context" => "roslyn_get_workspace_navigation_context",
            _ => throw new ArgumentException($"Unsupported bare verb for csharp: {name}")
        };
        return await roslyn.CallAsync(underlying, mapped).ConfigureAwait(false);
    }

    private static Dictionary<string, JsonElement> MapToRoslynArgs(
        string name,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var kv in args)
        {
            if (kv.Key is "language") continue;
            dict[kv.Key] = kv.Value;
        }

        if (name is "go_to_definition" or "find_usages" or "get_workspace_navigation_context")
        {
            if (!dict.ContainsKey("solution_or_project_path")
                || dict["solution_or_project_path"].ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(dict["solution_or_project_path"].GetString()))
            {
                var anchor = session.SolutionOrProjectPath
                    ?? throw new ArgumentException(
                        "solution_or_project_path required (or cdp_open a .sln/.csproj first).");
                dict["solution_or_project_path"] = JsonSerializer.SerializeToElement(anchor);
            }
        }
        else if (session.SolutionOrProjectPath is { Length: > 0 } sol
                 && !dict.ContainsKey("solution_or_project_path"))
        {
            dict["solution_or_project_path"] = JsonSerializer.SerializeToElement(sol);
        }

        return dict;
    }

    private static async Task<string> DispatchTypescriptAsync(
        string name,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var client = await EnsureTsClientAsync(session, cancellationToken).ConfigureAwait(false);
        JsonElement result = name switch
        {
            "go_to_definition" => await client.GoToDefinitionAsync(
                RequireString(args, "file_path"),
                RequireInt(args, "line"),
                RequireInt(args, "column"),
                cancellationToken).ConfigureAwait(false),
            "find_usages" => await client.FindUsagesAsync(
                RequireString(args, "file_path"),
                RequireInt(args, "line"),
                RequireInt(args, "column"),
                cancellationToken).ConfigureAwait(false),
            "get_document_symbols" => await client.GetDocumentSymbolsAsync(
                RequireString(args, "file_path"), cancellationToken).ConfigureAwait(false),
            "get_symbol_at_position" => await client.GetSymbolAtPositionAsync(
                RequireString(args, "file_path"),
                RequireInt(args, "line"),
                RequireInt(args, "column"),
                cancellationToken).ConfigureAwait(false),
            "get_diagnostics" => await client.GetDiagnosticsAsync(
                RequireString(args, "file_path"), cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentException($"Unsupported bare verb for typescript: {name}")
        };
        return result.GetRawText();
    }

    private static async Task<TypescriptLanguageClient> EnsureTsClientAsync(
        SessionContext session,
        CancellationToken cancellationToken)
    {
        if (session.ProjectRoot is not { Length: > 0 } root)
            throw new ArgumentException("cdp_open a typescript project first (tsconfig).");

        lock (TsGate)
        {
            if (_ts is { IsAlive: true } && string.Equals(_tsOpenedRoot, root, StringComparison.OrdinalIgnoreCase))
                return _ts;
        }

        var client = await TypescriptLanguageClient.StartAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await client.OpenProjectAsync(root, session.TsConfigPath, cancellationToken).ConfigureAwait(false);

        TypescriptLanguageClient? stale;
        lock (TsGate)
        {
            stale = _ts;
            _ts = client;
            _tsOpenedRoot = root;
        }

        if (stale is not null)
            await stale.DisposeAsync().ConfigureAwait(false);

        return client;
    }

    public static object? TsHealth()
    {
        lock (TsGate)
        {
            if (_ts is null)
                return new { warm = false, note = "not started (starts on first typescript IDE verb)" };
            return new
            {
                warm = _ts.IsAlive,
                worker_dir = _ts.WorkerDir,
                opened_root = _tsOpenedRoot,
                last_error = _ts.LastError
            };
        }
    }

    private static string RequireString(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.GetString() is not { Length: > 0 } s)
            throw new ArgumentException($"{key} (string) is required.");
        return s;
    }

    private static int RequireInt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || !el.TryGetInt32(out var n) || n < 1)
            throw new ArgumentException($"{key} (integer >= 1) is required.");
        return n;
    }
}
