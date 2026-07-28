using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cdp.Core;
using Cdp.Lsp;
using Cdp.ScriptableIde;
using TypescriptLang;
using Tool = ModelContextProtocol.Protocol.Tool;

namespace CdpMcp;
internal static partial class IdeLanguageTools
{
    public static async Task<string> DispatchBareAsync(string name, SessionContext session, IReadOnlyDictionary<string, ICdpBackendModule> byDomain, IReadOnlyDictionary<string, JsonElement> args, CancellationToken cancellationToken)
    {
        if (name == "resolve_project_root")
        {
            if (!args.TryGetValue("path", out var pEl) || pEl.GetString()is not { Length: > 0 } path)
            {
                return JsonSerializer.Serialize(new { session_project_root = session.ProjectRoot, session_kind = session.ProjectKind, session_language = session.Language, solution_or_project_path = session.SolutionOrProjectPath, tsconfig_path = session.TsConfigPath, scm_root = session.ScmRoot }, new JsonSerializerOptions { WriteIndented = true });
            }

            var detected = _langs.Detect(path);
            return JsonSerializer.Serialize(new { root = detected.Root, kind = detected.Kind, language = detected.Language, anchors = detected.Anchors, solution_or_project_path = detected.SolutionOrProjectPath, tsconfig_path = detected.TsConfigPath }, new JsonSerializerOptions { WriteIndented = true });
        }

        // Text find — language-agnostic, same shelf as get_completions (not cdp_buffer-only).
        if (name is "find" or "get_find" or "find_in_files" or "find_all")
            return await DispatchFindAsync(name, session, byDomain, args, cancellationToken).ConfigureAwait(false);
        // Verify-then-ship — inverse of put.
        if (name is "take" or "get_take")
            return await DispatchTakeAsync(session, byDomain, args, cancellationToken).ConfigureAwait(false);
        var lang = ResolveLanguage(session, args);
        if (name == "get_workspace_navigation_context")
        {
            if (!lang.Equals(CdpLanguages.Csharp, StringComparison.OrdinalIgnoreCase) && !CdpLanguages.IsAny(lang))
            {
                throw new ArgumentException("get_workspace_navigation_context is csharp-only in v0; open a .sln/.csproj scene (or language=csharp).");
            }

            return await DispatchCsharpAsync(name, session, byDomain, args, cancellationToken).ConfigureAwait(false);
        }

        if (lang.Equals(CdpLanguages.Csharp, StringComparison.OrdinalIgnoreCase))
        {
            if (name is "rename_symbol" or "code_actions" or "apply_code_action")
                throw new ArgumentException($"{name} via LSP is for languages with [[languages.lsp]] presets. For csharp use roslyn_rename / roslyn_get_code_actions.");
            return await DispatchCsharpAsync(name, session, byDomain, args, cancellationToken).ConfigureAwait(false);
        }

        if (lang.Equals(CdpLanguages.Typescript, StringComparison.OrdinalIgnoreCase))
        {
            // Worker covers map/diags/completions; rename/actions need LSP when preset exists.
            if (name is "rename_symbol" or "code_actions" or "apply_code_action")
            {
                if (LspPool.TryGetPreset(lang, out _))
                    return await DispatchLspAsync(name, lang, session, args, cancellationToken).ConfigureAwait(false);
                throw new ArgumentException($"{name} needs TypeScript LSP — cdp_settings op=lsp_ensure id=typescript (worker covers diags/completions).");
            }

            return await DispatchTypescriptAsync(name, session, args, cancellationToken).ConfigureAwait(false);
        }

        if (LspPool.TryGetPreset(lang, out _))
            return await DispatchLspAsync(name, lang, session, args, cancellationToken).ConfigureAwait(false);
        throw new ArgumentException($"No IDE engine for language '{lang}'. Call cdp_open(path) first, or configure [[languages.lsp]] / language=csharp|typescript|python.");
    }

    private static string ResolveLanguage(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        if (args.TryGetValue("language", out var el) && _langs.TryNormalize(el.GetString(), out var overrideLang) && !CdpLanguages.IsAny(overrideLang))
            return overrideLang;
        if (!CdpLanguages.IsAny(session.Language))
            return session.Language!;
        return CdpLanguages.Any;
    }

    static async Task<string> DispatchFindAsync(string name, SessionContext session, IReadOnlyDictionary<string, ICdpBackendModule> byDomain, IReadOnlyDictionary<string, JsonElement> args, CancellationToken cancellationToken)
    {
        if (_docStore is null)
            throw new InvalidOperationException("Document buffer store not bound.");
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var kv in args)
        {
            if (kv.Key is "language")
                continue;
            // bare file_path → buffer path=
            if (kv.Key is "file_path" && !args.ContainsKey("path"))
                dict["path"] = kv.Value;
            else
                dict[kv.Key] = kv.Value;
        }

        if (name is "find_in_files")
        {
            dict["op"] = JsonSerializer.SerializeToElement("find_all");
            if (!dict.ContainsKey("scope"))
                dict["scope"] = JsonSerializer.SerializeToElement("project");
        }
        else if (name is "find_all")
        {
            dict["op"] = JsonSerializer.SerializeToElement("find_all");
        }
        else
        {
            // find | get_find
            dict["op"] = JsonSerializer.SerializeToElement("find");
        }

        return await DocumentEditPlane.DispatchAsync("cdp_buffer", _docStore, session, byDomain, dict, cancellationToken).ConfigureAwait(false);
    }

    static async Task<string> DispatchTakeAsync(SessionContext session, IReadOnlyDictionary<string, ICdpBackendModule> byDomain, IReadOnlyDictionary<string, JsonElement> args, CancellationToken cancellationToken)
    {
        if (_docStore is null)
            throw new InvalidOperationException("Document buffer store not bound.");
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var kv in args)
        {
            if (kv.Key is "language")
                continue;
            if (kv.Key is "file_path" && !args.ContainsKey("path"))
                dict["path"] = kv.Value;
            else
                dict[kv.Key] = kv.Value;
        }

        dict["op"] = JsonSerializer.SerializeToElement("take");
        return await DocumentEditPlane.DispatchAsync("cdp_buffer", _docStore, session, byDomain, dict, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> DispatchCsharpAsync(string name, SessionContext session, IReadOnlyDictionary<string, ICdpBackendModule> byDomain, IReadOnlyDictionary<string, JsonElement> args, CancellationToken cancellationToken)
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
            "get_completions" => "roslyn_get_completions",
            "get_signature_help" => "roslyn_get_signature_help",
            "get_workspace_navigation_context" => "roslyn_get_workspace_navigation_context",
            _ => throw new ArgumentException($"Unsupported bare verb for csharp: {name}")};
        return await roslyn.CallAsync(underlying, mapped).ConfigureAwait(false);
    }

    private static Dictionary<string, JsonElement> MapToRoslynArgs(string name, SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var kv in args)
        {
            if (kv.Key is "language")
                continue;
            dict[kv.Key] = kv.Value;
        }

        if (name is "go_to_definition" or "find_usages" or "get_workspace_navigation_context" or "get_completions" or "get_signature_help")
        {
            if (!dict.ContainsKey("solution_or_project_path") || dict["solution_or_project_path"].ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(dict["solution_or_project_path"].GetString()))
            {
                var anchor = session.SolutionOrProjectPath ?? throw new ArgumentException("solution_or_project_path required (or cdp_open a .sln/.csproj first).");
                dict["solution_or_project_path"] = JsonSerializer.SerializeToElement(anchor);
            }
        }
        else if (session.SolutionOrProjectPath is { Length: > 0 } sol && !dict.ContainsKey("solution_or_project_path"))
        {
            dict["solution_or_project_path"] = JsonSerializer.SerializeToElement(sol);
        }

        if ((name is "get_completions" or "get_signature_help" or "get_diagnostics") && !dict.ContainsKey("source_text") && dict.TryGetValue("file_path", out var fpEl) && fpEl.GetString()is { Length: > 0 } fp && _docStore is not null)
        {
            try
            {
                var full = Path.GetFullPath(fp);
                var buf = _docStore.All.FirstOrDefault(b => string.Equals(b.Path, full, StringComparison.OrdinalIgnoreCase));
                if (buf is not null)
                    dict["source_text"] = JsonSerializer.SerializeToElement(buf.Text);
            }
            catch
            {
            // keep disk text
            }
        }

        return dict;
    }

    private static async Task<string> DispatchTypescriptAsync(string name, SessionContext session, IReadOnlyDictionary<string, JsonElement> args, CancellationToken cancellationToken)
    {
        var client = await EnsureTsClientAsync(session, cancellationToken).ConfigureAwait(false);
        JsonElement result = name switch
        {
            "go_to_definition" => await client.GoToDefinitionAsync(RequireString(args, "file_path"), RequireInt(args, "line"), RequireInt(args, "column"), cancellationToken).ConfigureAwait(false),
            "find_usages" => await client.FindUsagesAsync(RequireString(args, "file_path"), RequireInt(args, "line"), RequireInt(args, "column"), cancellationToken).ConfigureAwait(false),
            "get_document_symbols" => await client.GetDocumentSymbolsAsync(RequireString(args, "file_path"), cancellationToken).ConfigureAwait(false),
            "get_symbol_at_position" => await client.GetSymbolAtPositionAsync(RequireString(args, "file_path"), RequireInt(args, "line"), RequireInt(args, "column"), cancellationToken).ConfigureAwait(false),
            "get_diagnostics" => await client.GetDiagnosticsAsync(RequireString(args, "file_path"), cancellationToken).ConfigureAwait(false),
            "get_completions" or "get_signature_help" => throw new ArgumentException($"{name} is csharp-first (Roslyn). Open a .csproj/.sln with cdp_open; TS/LSP completion later."),
            _ => throw new ArgumentException($"Unsupported bare verb for typescript: {name}")};
        return result.GetRawText();
    }

    private static async Task<TypescriptLanguageClient> EnsureTsClientAsync(SessionContext session, CancellationToken cancellationToken)
    {
        if (session.ProjectRoot is not { Length: > 0 } root)
            throw new ArgumentException("cdp_open a typescript project first (tsconfig).");
        lock (TsGate)
        {
            if (_ts is { IsAlive: true } && string.Equals(_tsOpenedRoot, root, StringComparison.OrdinalIgnoreCase))
                return _ts;
        }

        var client = await TypescriptLanguageClient.StartAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
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
                return new
                {
                    warm = false,
                    note = "not started (starts on first typescript IDE verb)"
                };
            return new
            {
                warm = _ts.IsAlive,
                worker_dir = _ts.WorkerDir,
                opened_root = _tsOpenedRoot,
                last_error = _ts.LastError
            };
        }
    }
}