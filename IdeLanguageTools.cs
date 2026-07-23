using System.Text;
using System.Text.Json;
using Cdp.Core;
using Cdp.Lsp;
using Cdp.ScriptableIde;
using TypescriptLang;
using Tool = ModelContextProtocol.Protocol.Tool;

namespace CdpMcp;

/// <summary>
/// Bare IDE verbs + <c>cdp_open</c> helpers. Harness routes by <see cref="SessionContext.Language"/>.
/// csharp → Roslyn; typescript → TS worker; languages with [[languages.lsp]] → <see cref="LspClient"/>.
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
        "get_workspace_navigation_context",
        "rename_symbol",
        "code_actions",
        "apply_code_action"
    };

    private static readonly object TsGate = new();
    private static TypescriptLanguageClient? _ts;
    private static string? _tsOpenedRoot;
    private static LanguageRegistry _langs = LanguageRegistry.Default;
    private static readonly LspSessionPool LspPool = new();
    private static DocumentBufferStore? _docStore;

    public static void Configure(LanguageRegistry languages, IReadOnlyList<LspLaunchPreset>? lspPresets = null)
    {
        _langs = languages;
        LspPool.Configure(lspPresets ?? LspLaunchPreset.BuiltInDefaults);
    }

    public static void BindDocumentStore(DocumentBufferStore store) => _docStore = store;

    public static LanguageRegistry Languages => _langs;

    public static bool IsBareVerb(string name) => BareVerbs.Contains(name);

    public static IEnumerable<Tool> BuildBareVerbTools()
    {
        yield return Tool("go_to_definition",
            "IDE: go to definition. Routes by session language (Roslyn / TS worker / LSP). 1-based line/column.",
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
            "IDE: symbol / hover at position.",
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
            "IDE: Semantic Map related/subgraph (csharp). After cdp_open .sln/.csproj.",
            new
            {
                type = "object",
                properties = new
                {
                    file_path = new { type = "string" },
                    mode = new { type = "string", description = "related | subgraph" },
                    line = new { type = "integer" },
                    column = new { type = "integer" },
                    max_related = new { type = "integer" },
                    max_nodes = new { type = "integer" },
                    max_edges = new { type = "integer" },
                    include_kinds = new { type = "array", items = new { type = "string" } },
                    exclude_kinds = new { type = "array", items = new { type = "string" } },
                    preset = new { type = "string" },
                    language = new { type = "string" },
                    solution_or_project_path = new { type = "string" }
                },
                required = new[] { "file_path", "mode" }
            });
        yield return Tool("rename_symbol",
            "IDE: rename symbol (LSP textDocument/rename). language with [[languages.lsp]] preset (e.g. python).",
            new
            {
                type = "object",
                properties = new
                {
                    file_path = new { type = "string" },
                    line = new { type = "integer", description = "1-based" },
                    column = new { type = "integer", description = "1-based" },
                    new_name = new { type = "string" },
                    language = new { type = "string" },
                    apply = new { type = "boolean", description = "default true: write workspace edit to disk/buffers" }
                },
                required = new[] { "file_path", "line", "column", "new_name" }
            });
        yield return Tool("code_actions",
            "IDE: list LSP code actions / refactors at position. Then apply_code_action with action_index.",
            PositionalSchema());
        yield return Tool("apply_code_action",
            "IDE: apply code action by index from last code_actions on this LSP session.",
            new
            {
                type = "object",
                properties = new
                {
                    action_index = new { type = "integer", description = "0-based from code_actions" },
                    language = new { type = "string" },
                    apply = new { type = "boolean", description = "default true" }
                },
                required = new[] { "action_index" }
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
            recent_store = OpenRecentStore.Location,
            lsp_preset = LspPool.TryGetPreset(open.Language ?? "", out var p) ? p.Id : null
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    public static async Task CloseProjectAsync()
    {
        await LspPool.StopAllAsync().ConfigureAwait(false);
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
        {
            if (name is "rename_symbol" or "code_actions" or "apply_code_action")
                throw new ArgumentException(
                    $"{name} via LSP is for languages with [[languages.lsp]] presets. For csharp use roslyn_rename / roslyn_get_code_actions.");
            return await DispatchCsharpAsync(name, session, byDomain, args, cancellationToken).ConfigureAwait(false);
        }

        if (lang.Equals(CdpLanguages.Typescript, StringComparison.OrdinalIgnoreCase))
        {
            if (name is "rename_symbol" or "code_actions" or "apply_code_action")
                throw new ArgumentException($"{name} not wired for typescript worker yet; use LSP preset languages (e.g. python).");
            return await DispatchTypescriptAsync(name, session, args, cancellationToken).ConfigureAwait(false);
        }

        if (LspPool.TryGetPreset(lang, out _))
            return await DispatchLspAsync(name, lang, session, args, cancellationToken).ConfigureAwait(false);

        throw new ArgumentException(
            $"No IDE engine for language '{lang}'. Call cdp_open(path) first, or configure [[languages.lsp]] / language=csharp|typescript|python.");
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

    private static async Task<string> DispatchLspAsync(
        string name,
        string languageId,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var root = session.ProjectRoot
            ?? throw new ArgumentException("cdp_open a project first (e.g. pyproject.toml) for LSP languages.");
        var client = await LspPool.GetOrStartAsync(languageId, root, cancellationToken).ConfigureAwait(false);
        var langId = client.Preset.LanguageIds.FirstOrDefault() ?? languageId;

        if (name == "apply_code_action")
        {
            if (!args.TryGetValue("action_index", out var ai) || !ai.TryGetInt32(out var index) || index < 0)
                throw new ArgumentException("action_index (integer >= 0) is required.");

            var edit = await client.ApplyCodeActionAsync(index, cancellationToken).ConfigureAwait(false);
            var doApply = !args.TryGetValue("apply", out var ap) || ap.ValueKind != JsonValueKind.False;
            return FormatWorkspaceEditResult("apply_code_action", edit, doApply);
        }

        if (name == "rename_symbol")
        {
            var path = Path.GetFullPath(RequireString(args, "file_path"));
            await EnsureLspDocAsync(client, path, langId, cancellationToken).ConfigureAwait(false);
            var newName = RequireString(args, "new_name");
            var edit = await client.RenameAsync(
                path, RequireInt(args, "line"), RequireInt(args, "column"), newName, cancellationToken)
                .ConfigureAwait(false);
            var doApply = !args.TryGetValue("apply", out var ap) || ap.ValueKind != JsonValueKind.False;
            return FormatWorkspaceEditResult("rename_symbol", edit, doApply);
        }

        if (name is "go_to_definition" or "find_usages" or "get_symbol_at_position" or "code_actions")
        {
            var path = Path.GetFullPath(RequireString(args, "file_path"));
            await EnsureLspDocAsync(client, path, langId, cancellationToken).ConfigureAwait(false);
            var line = RequireInt(args, "line");
            var col = RequireInt(args, "column");

            return name switch
            {
                "go_to_definition" => JsonSerializer.Serialize(new
                {
                    schema = "lsp_locations/v0",
                    language = languageId,
                    locations = (await client.GoToDefinitionAsync(path, line, col, cancellationToken).ConfigureAwait(false))
                        .Select(LocDto)
                }, Pretty),
                "find_usages" => JsonSerializer.Serialize(new
                {
                    schema = "lsp_locations/v0",
                    language = languageId,
                    locations = (await client.FindReferencesAsync(path, line, col, cancellationToken).ConfigureAwait(false))
                        .Select(LocDto)
                }, Pretty),
                "get_symbol_at_position" => JsonSerializer.Serialize(new
                {
                    schema = "lsp_hover/v0",
                    language = languageId,
                    hover = await client.HoverAsync(path, line, col, cancellationToken).ConfigureAwait(false) is { } h
                        ? new
                        {
                            contents = h.Contents,
                            range = h.Range is { } r ? RangeDto(r) : null
                        }
                        : null
                }, Pretty),
                "code_actions" => JsonSerializer.Serialize(new
                {
                    schema = "lsp_code_actions/v0",
                    language = languageId,
                    actions = await client.CodeActionsAsync(path, line, col, cancellationToken).ConfigureAwait(false)
                }, Pretty),
                _ => throw new InvalidOperationException(name)
            };
        }

        if (name == "get_document_symbols")
        {
            var path = Path.GetFullPath(RequireString(args, "file_path"));
            await EnsureLspDocAsync(client, path, langId, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(new
            {
                schema = "lsp_document_symbols/v0",
                language = languageId,
                symbols = await client.DocumentSymbolsAsync(path, cancellationToken).ConfigureAwait(false)
            }, Pretty);
        }

        if (name == "get_diagnostics")
        {
            var path = Path.GetFullPath(RequireString(args, "file_path"));
            await EnsureLspDocAsync(client, path, langId, cancellationToken).ConfigureAwait(false);
            // brief wait for publishDiagnostics
            await Task.Delay(400, cancellationToken).ConfigureAwait(false);
            var diags = await client.DiagnosticsAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(new
            {
                schema = "lsp_diagnostics/v0",
                language = languageId,
                diagnostics = diags.Select(d => new
                {
                    severity = d.Severity,
                    code = d.Code,
                    message = d.Message,
                    source = d.Source,
                    range = RangeDto(d.Range)
                })
            }, Pretty);
        }

        throw new ArgumentException($"Unsupported bare verb for LSP language '{languageId}': {name}");
    }

    static async Task EnsureLspDocAsync(LspClient client, string path, string languageId, CancellationToken ct)
    {
        string? text = null;
        if (_docStore?.TryGet(path, out var buf) == true)
            text = buf.Text;
        await client.EnsureOpenAsync(path, text, languageId, ct).ConfigureAwait(false);
    }

    static object LocDto(LspLocation loc)
    {
        var (ls, cs) = LspClient.ToOneBased(loc.Range.Start);
        var (le, ce) = LspClient.ToOneBased(loc.Range.End);
        return new
        {
            path = LspClient.UriToPath(loc.Uri),
            uri = loc.Uri,
            start_line = ls,
            start_column = cs,
            end_line = le,
            end_column = ce
        };
    }

    static object RangeDto(LspRange r)
    {
        var (ls, cs) = LspClient.ToOneBased(r.Start);
        var (le, ce) = LspClient.ToOneBased(r.End);
        return new { start_line = ls, start_column = cs, end_line = le, end_column = ce };
    }

    static string FormatWorkspaceEditResult(string op, LspWorkspaceEdit? edit, bool apply)
    {
        if (edit is null)
            return JsonSerializer.Serialize(new { schema = "lsp_workspace_edit/v0", op, ok = false, note = "no_edit" }, Pretty);

        var files = new List<object>();
        if (apply)
        {
            foreach (var (uri, edits) in edit.Changes)
            {
                var path = LspClient.UriToPath(uri);
                var applied = ApplyEditsToFile(path, edits);
                files.Add(new { path, edits = edits.Count, applied });
            }
        }
        else
        {
            foreach (var (uri, edits) in edit.Changes)
                files.Add(new { path = LspClient.UriToPath(uri), edits = edits.Count, applied = false });
        }

        return JsonSerializer.Serialize(new
        {
            schema = "lsp_workspace_edit/v0",
            op,
            ok = true,
            apply,
            files
        }, Pretty);
    }

    static bool ApplyEditsToFile(string path, IReadOnlyList<LspTextEdit> edits)
    {
        var text = _docStore?.TryGet(path, out var buf) == true
            ? buf.Text
            : File.ReadAllText(path);

        // Apply from end to start so offsets stay valid
        var ordered = edits
            .Select(e =>
            {
                var start = OffsetOf(text, e.Range.Start.Line + 1, e.Range.Start.Character + 1);
                var end = OffsetOf(text, e.Range.End.Line + 1, e.Range.End.Character + 1);
                return (start, end, e.NewText);
            })
            .OrderByDescending(t => t.start)
            .ToArray();

        var sb = new StringBuilder(text);
        foreach (var (start, end, newText) in ordered)
        {
            if (end < start || start < 0 || end > sb.Length)
                continue;
            sb.Remove(start, end - start);
            sb.Insert(start, newText);
        }

        var next = sb.ToString();
        if (_docStore?.TryGet(path, out var openBuf) == true)
        {
            openBuf.Text = next;
            openBuf.Version++;
            openBuf.Dirty = true;
            _docStore.Flush(openBuf, allowShrink: true);
        }
        else
        {
            File.WriteAllText(path, next);
        }

        return true;
    }

    static int OffsetOf(string text, int line1Based, int column1Based)
    {
        // LSP character is UTF-16; match DocumentBufferStore semantics (1-based line/col).
        var line = 1;
        var i = 0;
        while (i < text.Length && line < line1Based)
        {
            if (text[i] == '\n') line++;
            i++;
        }

        var col = 1;
        while (i < text.Length && col < column1Based)
        {
            if (text[i] == '\n') break;
            i++;
            col++;
        }

        return i;
    }

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

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

    public static object LspHealth() => new
    {
        presets = LspPool.Presets.Select(p => new { p.Id, p.Command, args = p.Args }),
        sessions = LspPool.HealthSnapshot()
    };

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
