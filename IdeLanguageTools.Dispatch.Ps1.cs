#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.Lsp;

namespace CdpMcp;

/// <summary>PowerShell bare-verb dispatch: PSES LSP when preset mounted, parser fallback for syntax-only.</summary>
internal static partial class IdeLanguageTools
{
    static async Task<string> DispatchPowerShellAsync(
        string name,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        if (LspPool.TryGetPreset(CdpLanguages.PowerShell, out _))
        {
            if (name is "get_diagnostics" or "go_to_definition" or "find_usages"
                or "get_document_symbols" or "get_symbol_at_position" or "rename_symbol"
                or "code_actions" or "apply_code_action" or "get_completions" or "get_signature_help")
                return await DispatchLspAsync(name, CdpLanguages.PowerShell, session, args, cancellationToken)
                    .ConfigureAwait(false);
        }

        if (name == "get_diagnostics")
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

        throw new ArgumentException(
            $"No IDE engine for powershell op '{name}'. Mount PSES: cdp_settings op=lsp_ensure id=powershell");
    }
}
