using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// Verify-then-ship: inverse of <c>put</c>. Body + chat_markdown for the human reply.
/// </summary>
internal static class TakeShip
{
    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static async Task<string> TakeAsync(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        Func<DocBuffer, DocumentBufferStore, SessionContext, IReadOnlyDictionary<string, ICdpBackendModule>, bool, string, CancellationToken, Task<(object? diagnostics, string? note)>> diagnose,
        CancellationToken cancellationToken)
    {
        var span = EditorComfort.ResolveTakeSpan(store, session, args);
        var buf = span.Buf;
        var body = span.Body;
        EditorComfort.RememberFile(buf.Path);
        EditorComfort.PushLocus(session, span.From);

        var fence = TakeKinds.ResolveFence(buf, OptString(args, "fence") ?? OptString(args, "kind"));
        var doCheck = BoolOr(args, "check", defaultValue: true);
        // force=true → ship despite verify errors (not diagnostics recompute).
        var forceShip = BoolOr(args, "force", defaultValue: false);

        object verify;
        var errorCount = 0;
        string verifyStatus;
        object? verifyItems = null;
        string? verifyNote = null;

        if (!doCheck)
        {
            verifyStatus = "skipped";
            verifyNote = "check=false";
            verify = new { status = verifyStatus, error_count = 0, note = verifyNote };
        }
        else if (TakeKinds.HasIdeDiagnostics(buf))
        {
            var scope = OptString(args, "scope") ?? "syntax";
            if (CsxBufferDiagnostics.IsCsxPath(buf.Path)
                && (scope is "syntax" or "csx" or "script" or "parse" or "file"))
                scope = CsxBufferDiagnostics.Scope;

            var flush = BoolOr(args, "flush", defaultValue: true);
            var (diagnostics, note) = await diagnose(
                    buf, store, session, byDomain, flush, scope, cancellationToken)
                .ConfigureAwait(false);

            if (diagnostics is null)
            {
                // No online diags available → honest skip (e.g. unsupported), still ship.
                verifyStatus = "skipped";
                verifyNote = note ?? "no_diagnostics";
                verify = new { status = verifyStatus, error_count = 0, note = verifyNote };
            }
            else
            {
                errorCount = TakeKinds.CountErrors(diagnostics);
                verifyStatus = errorCount > 0 ? "failed" : "ok";
                verifyItems = diagnostics;
                verifyNote = note;
                verify = new
                {
                    status = verifyStatus,
                    error_count = errorCount,
                    items = diagnostics,
                    note = note
                };
            }
        }
        else if (string.Equals(Path.GetExtension(buf.Path), ".json", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(fence, "json", StringComparison.OrdinalIgnoreCase))
        {
            var jr = TakeKinds.VerifyJson(body);
            verifyStatus = jr.Status;
            errorCount = jr.ErrorCount;
            verifyItems = jr.Items;
            verifyNote = jr.Note;
            verify = new
            {
                status = verifyStatus,
                error_count = errorCount,
                items = verifyItems,
                note = verifyNote
            };
        }
        else
        {
            verifyStatus = "skipped";
            verifyNote = "no_kind_checker";
            verify = new { status = verifyStatus, error_count = 0, note = verifyNote };
        }

        var preview = TakeKinds.PreviewAscii(buf, body);
        var chat = TakeKinds.ChatMarkdown(fence, body);

        if (verifyStatus == "failed" && !forceShip)
        {
            return JsonSerializer.Serialize(new
            {
                schema = EditorComfort.Schema,
                ok = false,
                op = "take",
                error = "verify_failed",
                path = buf.Path,
                from = span.From,
                fence,
                kind = fence,
                body,
                chat_markdown = chat,
                preview_ascii = preview.Ascii,
                preview = new { status = preview.Status, note = preview.Note },
                verify,
                start_line = span.StartLine,
                start_column = span.StartCol,
                end_line = span.EndLine,
                end_column = span.EndCol,
                lines = CountLines(body),
                next = new object[]
                {
                    new { go = "edit_draft", label = "Fix then take", why = "verify failed — edit buffer" },
                    new { go = "take", label = "Force ship", why = "force=true despite errors" },
                    new { go = "diagnostics", label = "Diagnostics", why = "full diag card" }
                },
                hint =
                    "Verify failed — fix in IDE then take again. force=true ships anyway. " +
                    "Paste chat_markdown into the assistant reply when ok."
            }, Pretty);
        }

        return JsonSerializer.Serialize(new
        {
            schema = EditorComfort.Schema,
            ok = true,
            op = "take",
            path = buf.Path,
            from = span.From,
            fence,
            kind = fence,
            body,
            chat_markdown = chat,
            preview_ascii = preview.Ascii,
            preview = new { status = preview.Status, note = preview.Note },
            verify,
            forced = forceShip && verifyStatus == "failed",
            start_line = span.StartLine,
            start_column = span.StartCol,
            end_line = span.EndLine,
            end_column = span.EndCol,
            lines = CountLines(body),
            chars = body.Length,
            meta = buf.ToMeta(),
            next = new object[]
            {
                new { go = "copy", label = "Copy to clipboard", why = "frame for paste elsewhere" },
                new { go = "put", label = "Put rewrite", why = "inverse — dump new draft" },
                new { go = "edit_draft", label = "Keep editing", why = "not done" }
            },
            hint =
                "Paste chat_markdown into the assistant reply (MCP cannot push chat). " +
                "Inverse of put. check=false skips verify; force=true ships despite errors."
        }, Pretty);
    }

    static int CountLines(string text)
    {
        if (text.Length == 0) return 1;
        var n = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') n++;
        }

        return n;
    }

    static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    static bool BoolOr(IReadOnlyDictionary<string, JsonElement> args, string key, bool defaultValue)
    {
        if (!args.TryGetValue(key, out var el))
            return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(el.GetString(), out var b) => b,
            _ => defaultValue
        };
    }
}
