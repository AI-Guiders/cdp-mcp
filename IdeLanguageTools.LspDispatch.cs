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
    private static async Task<string> DispatchLspAsync(string name, string languageId, SessionContext session, IReadOnlyDictionary<string, JsonElement> args, CancellationToken cancellationToken)
    {
        var root = session.ProjectRoot ?? throw new ArgumentException("cdp_open a project first (e.g. pyproject.toml) for LSP languages.");
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
            var edit = await client.RenameAsync(path, RequireInt(args, "line"), RequireInt(args, "column"), newName, cancellationToken).ConfigureAwait(false);
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
                "go_to_definition" => JsonSerializer.Serialize(new { schema = "lsp_locations/v0", language = languageId, locations = (await client.GoToDefinitionAsync(path, line, col, cancellationToken).ConfigureAwait(false)).Select(LocDto) }, Pretty),
                "find_usages" => JsonSerializer.Serialize(new { schema = "lsp_locations/v0", language = languageId, locations = (await client.FindReferencesAsync(path, line, col, cancellationToken).ConfigureAwait(false)).Select(LocDto) }, Pretty),
                "get_symbol_at_position" => JsonSerializer.Serialize(new { schema = "lsp_hover/v0", language = languageId, hover = await client.HoverAsync(path, line, col, cancellationToken).ConfigureAwait(false)is { } h ? new { contents = h.Contents, range = h.Range is { } r ? RangeDto(r) : null } : null }, Pretty),
                "code_actions" => JsonSerializer.Serialize(new { schema = "lsp_code_actions/v0", language = languageId, actions = await client.CodeActionsAsync(path, line, col, cancellationToken).ConfigureAwait(false) }, Pretty),
                _ => throw new InvalidOperationException(name)};
        }

        if (name == "get_document_symbols")
        {
            var path = Path.GetFullPath(RequireString(args, "file_path"));
            await EnsureLspDocAsync(client, path, langId, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(new { schema = "lsp_document_symbols/v0", language = languageId, symbols = await client.DocumentSymbolsAsync(path, cancellationToken).ConfigureAwait(false) }, Pretty);
        }

        if (name == "get_diagnostics")
        {
            var path = Path.GetFullPath(RequireString(args, "file_path"));
            await EnsureLspDocAsync(client, path, langId, cancellationToken).ConfigureAwait(false);
            // brief wait for publishDiagnostics
            await Task.Delay(400, cancellationToken).ConfigureAwait(false);
            var diags = await client.DiagnosticsAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(new { schema = "lsp_diagnostics/v0", language = languageId, diagnostics = diags.Select(d => new { severity = d.Severity, code = d.Code, message = d.Message, source = d.Source, range = RangeDto(d.Range) }) }, Pretty);
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
        var(ls, cs) = LspClient.ToOneBased(loc.Range.Start);
        var(le, ce) = LspClient.ToOneBased(loc.Range.End);
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
        var(ls, cs) = LspClient.ToOneBased(r.Start);
        var(le, ce) = LspClient.ToOneBased(r.End);
        return new
        {
            start_line = ls,
            start_column = cs,
            end_line = le,
            end_column = ce
        };
    }

    static string FormatWorkspaceEditResult(string op, LspWorkspaceEdit? edit, bool apply)
    {
        if (edit is null)
            return JsonSerializer.Serialize(new { schema = "lsp_workspace_edit/v0", op, ok = false, note = "no_edit" }, Pretty);
        var files = new List<object>();
        if (apply)
        {
            foreach (var(uri, edits)in edit.Changes)
            {
                var path = LspClient.UriToPath(uri);
                var applied = ApplyEditsToFile(path, edits);
                files.Add(new { path, edits = edits.Count, applied });
            }
        }
        else
        {
            foreach (var(uri, edits)in edit.Changes)
                files.Add(new { path = LspClient.UriToPath(uri), edits = edits.Count, applied = false });
        }

        return JsonSerializer.Serialize(new { schema = "lsp_workspace_edit/v0", op, ok = true, apply, files }, Pretty);
    }

    static bool ApplyEditsToFile(string path, IReadOnlyList<LspTextEdit> edits)
    {
        var text = _docStore?.TryGet(path, out var buf) == true ? buf.Text : File.ReadAllText(path);
        // Apply from end to start so offsets stay valid
        var ordered = edits.Select(e =>
        {
            var start = OffsetOf(text, e.Range.Start.Line + 1, e.Range.Start.Character + 1);
            var end = OffsetOf(text, e.Range.End.Line + 1, e.Range.End.Character + 1);
            return (start, end, e.NewText);
        }).OrderByDescending(t => t.start).ToArray();
        var sb = new StringBuilder(text);
        foreach (var(start, end, newText)in ordered)
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
            if (text[i] == '\n')
                line++;
            i++;
        }

        var col = 1;
        while (i < text.Length && col < column1Based)
        {
            if (text[i] == '\n')
                break;
            i++;
            col++;
        }

        return i;
    }
}