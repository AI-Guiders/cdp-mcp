#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.Lsp;

namespace CdpMcp;

/// <summary>
/// PowerShell bare verbs — capability split (no LSP↔parser fallback chain).
/// Syntax diagnostics: Parser AST (same contract as <c>cdp_buffer</c>; Jul-23 first-class PS).
/// Language service: PSES LSP for completion, navigation, rename, code actions.
/// </summary>
internal static partial class IdeLanguageTools
{
    static readonly HashSet<string> Ps1LspVerbs = new(StringComparer.Ordinal)
    {
        "go_to_definition",
        "find_usages",
        "get_document_symbols",
        "get_symbol_at_position",
        "rename_symbol",
        "code_actions",
        "apply_code_action",
        "get_completions",
        "get_signature_help"
    };

    static async Task<string> DispatchPowerShellAsync(
        string name,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        if (name == "get_diagnostics")
            return await DiagnosePowerShellParserAsync(session, args, cancellationToken).ConfigureAwait(false);

        if (!Ps1LspVerbs.Contains(name))
            throw new ArgumentException($"No IDE engine for powershell op '{name}'.");

        if (!LspPool.TryGetPreset(CdpLanguages.PowerShell, out _))
        {
            throw new ArgumentException(
                $"powershell op '{name}' needs PSES — cdp_settings op=lsp_ensure id=powershell.");
        }

        return await DispatchLspAsync(name, CdpLanguages.PowerShell, session, args, cancellationToken)
            .ConfigureAwait(false);
    }

    static async Task<string> DiagnosePowerShellParserAsync(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var filePath = RequireString(args, "file_path");
        string? sourceText = args.TryGetValue("source_text", out var stEl) ? stEl.GetString() : null;
        if (sourceText is null && _docStore is not null)
        {
            var open = _docStore.All.FirstOrDefault(b =>
                string.Equals(b.Path, filePath, StringComparison.OrdinalIgnoreCase));
            sourceText = open?.Text;
        }

        if (sourceText is null && File.Exists(filePath))
            sourceText = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        return await Ps1BufferDiagnostics.DiagnoseAsync(
                filePath,
                sourceText ?? "",
                session.ProjectRoot,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
