using System.Text.Json;
using AIGuiders.Platform.Execution.Language;
using AIGuiders.Platform.Modeling.Language;
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdeLanguageTools
{
    private static readonly JsonSerializerOptions LrcJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly HashSet<string> LrcBareVerbs = new(StringComparer.Ordinal)
    {
        "get_diagnostics",
        "get_document_symbols",
        "go_to_definition",
        "find_usages",
        "get_completions",
        "get_symbol_at_position",
        "rename_symbol",
    };

    static bool IsLrcLanguage(string? languageId) => BufferLanguageRules.IsLrcLanguage(languageId);

    static bool TryGetExplicitLanguage(IReadOnlyDictionary<string, JsonElement> args, out string language)
    {
        language = "";
        if (!args.TryGetValue("language", out var el))
            return false;
        if (!_langs.TryNormalize(el.GetString(), out var normalized) || CdpLanguages.IsAny(normalized))
            return false;
        language = normalized;
        return true;
    }

    static void RefuseWrongEnginePairing(string lang, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        var pathLang = LanguagePathRules.ResolveLanguageId(filePath);
        if (pathLang is null)
            return;

        if (pathLang.Equals(LanguageIds.Fsharp, StringComparison.OrdinalIgnoreCase)
            && lang.Equals(CdpLanguages.Csharp, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Refusing csharp engine for .fs — open the file with cdp_open or set language=fsharp.");
        }

        if (pathLang.Equals(LanguageIds.Gdl, StringComparison.OrdinalIgnoreCase)
            && lang.Equals(CdpLanguages.Csharp, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Refusing csharp engine for .gdl — open the file with cdp_open or set language=gdl.");
        }
    }

    static async Task<string> DispatchLrcAsync(
        string name,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        if (!LrcBareVerbs.Contains(name))
            throw new ArgumentException($"{name} is not supported via LRC v1 (fsharp/gdl).");

        var filePath = RequireString(args, "file_path");
        var req = BuildLanguageRequest(session, filePath, args);
        var renameReq = name == "rename_symbol"
            ? BuildRenameRequest(session, filePath, args)
            : null;

        try
        {
            var result = await CdpLanguageResolverGate.DispatchAsync(
                name, session, req, renameReq, cancellationToken).ConfigureAwait(false);

            var raw = SerializeLrcResult(result);
            return name == "get_diagnostics"
                ? DiagnosticAnchorWires.Enrich(raw, session.ProjectRoot, filePath)
                : raw;
        }
        catch (Cdp.Core.DiagnosticException diag)
        {
            return System.Text.Json.JsonSerializer.Serialize(
                new { diagnostic = diag.Envelope, ok = false },
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        }
    }

    static AIGuiders.Platform.Execution.Language.LanguageRequest BuildLanguageRequest(
        SessionContext session,
        string filePath,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var line = args.TryGetValue("line", out var lineEl) && lineEl.TryGetInt32(out var l) ? l : 1;
        var column = args.TryGetValue("column", out var colEl) && colEl.TryGetInt32(out var c) ? c : 1;
        string? sourceText = null;

        if (args.TryGetValue("source_text", out var srcEl) && srcEl.ValueKind == JsonValueKind.String)
            sourceText = srcEl.GetString();

        if (sourceText is null && _docStore is not null)
        {
            try
            {
                var full = Path.GetFullPath(filePath);
                var buf = _docStore.All.FirstOrDefault(b => string.Equals(b.Path, full, StringComparison.OrdinalIgnoreCase));
                sourceText = buf?.Text;
            }
            catch
            {
                // disk text via backend
            }
        }

        var solution = session.SolutionOrProjectPath;
        if (args.TryGetValue("solution_or_project_path", out var solEl)
            && solEl.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(solEl.GetString()))
        {
            solution = solEl.GetString();
        }

        return new AIGuiders.Platform.Execution.Language.LanguageRequest(filePath, line, column, sourceText, solution);
    }

    static RenameSymbolRequest BuildRenameRequest(
        SessionContext session,
        string filePath,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var req = BuildLanguageRequest(session, filePath, args);
        var newName = RequireString(args, "new_name");
        var apply = args.TryGetValue("apply", out var applyEl)
            && applyEl.ValueKind == JsonValueKind.True;
        return new RenameSymbolRequest(req, newName, apply);
    }
}
