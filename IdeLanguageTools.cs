using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
internal static partial class IdeLanguageTools
{
    private static readonly HashSet<string> BareVerbs = new(StringComparer.Ordinal)
    {
        "go_to_definition",
        "find_usages",
        "get_document_symbols",
        "get_symbol_at_position",
        "get_diagnostics",
        "get_completions",
        "get_signature_help",
        "find",
        "get_find",
        "find_in_files",
        "find_all",
        "take",
        "get_take",
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

    /// <summary>Hot-reload LSP presets after Options install/add (no MCP remount).</summary>
    public static void ReconfigureLsp(IReadOnlyList<LspLaunchPreset> presets) =>
        LspPool.Configure(presets.Count > 0 ? presets : LspLaunchPreset.BuiltInDefaults);

    public static IReadOnlyList<LspLaunchPreset> CurrentLspPresets => LspPool.Presets;

    public static void BindDocumentStore(DocumentBufferStore store) => _docStore = store;

    /// <summary>Citizen replace host-execute — open + ApplyReplace + Flush (PathMutateGate).</summary>
    public static bool TryReplaceInDocument(
        string path,
        string? projectRoot,
        string oldString,
        string newString,
        out string? fullPath,
        out string? docId,
        out string? error)
    {
        fullPath = null;
        docId = null;
        error = null;
        if (_docStore is null)
        {
            error = "doc_store_unbound";
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "path_empty";
            return false;
        }

        if (string.IsNullOrEmpty(oldString))
        {
            error = "old_empty";
            return false;
        }

        try
        {
            var resolved = ResolveOpenPath(path.Trim(), projectRoot);
            var buf = _docStore.Open(resolved);
            _docStore.ApplyReplace(buf, oldString, newString ?? "");
            _docStore.Flush(buf, allowShrink: true);
            fullPath = buf.Path;
            docId = buf.DocId;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    /// <summary>Citizen route host / buffer open — relative path resolves under projectRoot.</summary>
    public static bool TryOpenDocument(
        string path,
        string? projectRoot,
        out string? fullPath,
        out string? docId,
        out string? error)
    {
        fullPath = null;
        docId = null;
        error = null;
        if (_docStore is null)
        {
            error = "doc_store_unbound";
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "path_empty";
            return false;
        }

        try
        {
            var resolved = ResolveOpenPath(path.Trim(), projectRoot);
            var buf = _docStore.Open(resolved);
            fullPath = buf.Path;
            docId = buf.DocId;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    static string ResolveOpenPath(string path, string? projectRoot)
    {
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);
        var root = string.IsNullOrWhiteSpace(projectRoot)
            ? Environment.CurrentDirectory
            : projectRoot.Trim();
        return Path.GetFullPath(Path.Combine(root, path));
    }


    public static LanguageRegistry Languages => _langs;

    public static bool IsBareVerb(string name) => BareVerbs.Contains(name);


    public static string ApplyOpen(
        SessionContext session,
        ProjectOpenResult open,
        DocumentBufferStore.BufferParkResult? bufferPark = null)
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
        var scmNote = GitSessionDefaults.DescribeAncestorScmRisk(open.Root, session.ScmRoot);
        var park = bufferPark ?? DocumentBufferStore.BufferParkResult.Empty;
        return JsonSerializer.Serialize(new
        {
            root = open.Root,
            kind = open.Kind,
            language = open.Language,
            anchors = open.Anchors,
            solution_or_project_path = open.SolutionOrProjectPath,
            tsconfig_path = open.TsConfigPath,
            scm_root = session.ScmRoot,
            scm_risk = scmNote is null ? null : "ancestor",
            scm_note = scmNote,
            buffers_parked = park.ClosedClean,
            buffers_kept_dirty = park.KeptDirty.Count,
            buffers_kept_dirty_paths = park.KeptDirty.Count == 0 ? null : park.KeptDirty,
            buffer_note = park.Note,
            session_phase = CdpEnumParse.ToWire(session.Phase),
            session_object = CdpEnumParse.ToWire(session.Object),
            recent_count = OpenRecentStore.List().Count,
            recent_store = OpenRecentStore.Location,
            lsp_preset = LspPool.TryGetPreset(open.Language ?? "", out var p) ? p.Id : null
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Attach ApplyOpen meta onto create/sln step JSON as <c>open</c> (scm_note, buffer_note, …).</summary>
    public static string MergeStepOpenMeta(string stepJson, string? openPayloadJson)
    {
        if (string.IsNullOrWhiteSpace(openPayloadJson))
            return stepJson;
        try
        {
            var node = JsonNode.Parse(stepJson)?.AsObject();
            var open = JsonNode.Parse(openPayloadJson);
            if (node is null || open is null)
                return stepJson;
            node["open"] = open;
            return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return stepJson;
        }
    }



    public static async Task CloseProjectAsync()
    {
        await LspPool.StopAllAsync().ConfigureAwait(false);
    }



    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };


    public static object LspHealth() => new
    {
        presets = LspPool.Presets.Select(p => new
        {
            p.Id,
            p.Command,
            candidates = p.CommandCandidates,
            args = p.Args,
            resolved_probe = TryProbeResolve(p)
        }),
        sessions = LspPool.HealthSnapshot(),
        note = "Python default prefers basedpyright-langserver (richer codeAction) over pyright-langserver."
    };

    static object? TryProbeResolve(LspLaunchPreset p)
    {
        try
        {
            var r = LspCommandResolver.Resolve(p);
            return new { file = r.FileName, display = r.Display, args = r.Args };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
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
