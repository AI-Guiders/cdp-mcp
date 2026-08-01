using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// Verify-then-ship into agent context (inverse of <c>put</c>). Body + chat_markdown.
/// For operator delivery without loading body into the agent — use <see cref="IdeShare"/> / <c>share</c>.
/// Helpers → <c>TakeShip.Helpers.cs</c>.
/// </summary>
internal static partial class TakeShip
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
            // Under open project root → semantic (project); scratch/orphan → syntax (no MSBuild tax).
            var scope = OptString(args, "scope") ?? DefaultVerifyScope(session, buf);
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

        // PlantUML: render PNG for verify + preview_path. ImageContent only if agent asks (vision|see).
        object preview;
        string? previewAscii = null;
        var wantVision = BoolOr(args, "vision", defaultValue: false) || BoolOr(args, "see", defaultValue: false);
        var plantLike = PlantUmlRender.LooksLikePlantUml(body, buf.Path, fence);
        if (plantLike)
        {
            var rendered = PlantUmlRender.RenderPng(body, cancellationToken);
            if (rendered.Ok && rendered.Png is { Length: > 0 } png)
            {
                var previewPath = TryWritePreviewPng(buf.Path, png);
                var attached = false;
                if (wantVision)
                    attached = ToolMediaOutbox.TryAdd(png, "image/png");

                preview = new
                {
                    status = "ok",
                    kind = "plantuml_png",
                    bytes = png.Length,
                    mime = "image/png",
                    preview_path = previewPath,
                    attached_image = attached,
                    vision_requested = wantVision,
                    jar = rendered.Jar,
                    note = wantVision
                        ? (attached
                            ? "ImageContent attached (agent vision=true)"
                            : "vision=true but outbox full — Read preview_path")
                        : "PNG on disk; agent: vision=true to attach ImageContent, or Read preview_path"
                };
                if (doCheck && verifyStatus is "skipped")
                {
                    verifyStatus = "ok";
                    verifyNote = "plantuml_render";
                    verify = new { status = verifyStatus, error_count = 0, note = verifyNote };
                }
            }
            else
            {
                preview = new
                {
                    status = "failed",
                    kind = "plantuml_png",
                    note = rendered.Error,
                    jar = rendered.Jar
                };
                if (doCheck)
                {
                    verifyStatus = "failed";
                    errorCount = Math.Max(1, errorCount);
                    verifyItems = new object[] { new { severity = "error", message = rendered.Error } };
                    verifyNote = "plantuml_render";
                    verify = new
                    {
                        status = verifyStatus,
                        error_count = errorCount,
                        items = verifyItems,
                        note = verifyNote
                    };
                }
            }
        }
        else
        {
            var ascii = TakeKinds.PreviewAscii(buf, body);
            previewAscii = ascii.Ascii;
            preview = new { status = ascii.Status, note = ascii.Note };
        }

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
                preview_ascii = previewAscii,
                preview,
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
            preview_ascii = previewAscii,
            preview,
            verify,
            forced = forceShip && verifyStatus == "failed",
            start_line = span.StartLine,
            start_column = span.StartCol,
            end_line = span.EndLine,
            end_column = span.EndCol,
            lines = CountLines(body),
            chars = body.Length,
            meta = buf.ToMeta(),
            next = BuildTakeNext(plantLike, wantVision),
            hint =
                "Paste chat_markdown into the assistant reply (MCP cannot push chat). " +
                "Prefer share with=operator to deliver to human without loading body here. " +
                "Inverse of put. check=false skips verify; force=true ships despite errors. " +
                (plantLike
                    ? "Diagram: vision=true|see=true attaches ImageContent for the agent; else Read preview_path."
                    : "")
        }, Pretty);
    }
}
