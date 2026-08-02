using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using CdpMcp.Cockpit.Surface;

namespace CdpMcp;

internal static partial class EditorPlane
{
    static async Task<string> ApplyAsync(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var slices = TryGetSlices(args)
            ?? throw new ArgumentException(
                "cdp_edit_plan apply requires yaml= (YAML list) or slices=[{message,steps:…}].");
        var stopOnError = BoolOr(args, "stop_on_error", defaultValue: true);
        var diagnose = BoolOr(args, "diagnose", defaultValue: true);
        var flush = BoolOr(args, "flush", defaultValue: true);
        var skipValidate = BoolOr(args, "skip_validate", defaultValue: false);

        if (!skipValidate)
        {
            var pre = await ValidateAsync(store, session, byDomain, args, cancellationToken)
                .ConfigureAwait(false);
            using var doc = JsonDocument.Parse(pre);
            if (doc.RootElement.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False)
            {
                return JsonSerializer.Serialize(new
                {
                    schema = PlanSchema,
                    op = "apply",
                    ok = false,
                    error = "validate_failed",
                    hint = "Fix slices or pass skip_validate=true (not recommended).",
                    validate = JsonSerializer.Deserialize<JsonElement>(pre)
                }, Pretty);
            }
        }

        var sliceResults = new List<object>();
        var anyFail = false;

        foreach (var slice in slices.Take(MaxSlices))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stepResults = new List<object>();
            object? fixResult = null;
            var sliceOk = true;
            string? sliceError = null;

            // fix: first (code actions), then mutate steps
            if (slice.FixIds.Count > 0 && !string.IsNullOrWhiteSpace(slice.Path))
            {
                try
                {
                    var full = ResolveUserPath(session, slice.Path);
                    var (okHits, errHits, hits) = await EditPlanFix.FindHitsAsync(
                            session, byDomain, full, slice.FixIds, cancellationToken)
                        .ConfigureAwait(false);
                    if (!okHits)
                    {
                        sliceOk = false;
                        anyFail = true;
                        sliceError = errHits;
                        fixResult = new { ok = false, error = errHits };
                    }
                    else
                    {
                        var (okApply, errApply, detail) = await EditPlanFix.ApplyHitsAsync(
                                session, byDomain, full, hits, cancellationToken)
                            .ConfigureAwait(false);
                        fixResult = new { ok = okApply, error = errApply, applied = detail };
                        if (!okApply)
                        {
                            sliceOk = false;
                            anyFail = true;
                            sliceError = errApply;
                        }
                    }
                }
                catch (Exception ex)
                {
                    sliceOk = false;
                    anyFail = true;
                    sliceError = ex.Message;
                    fixResult = new { ok = false, error = ex.Message };
                }

                if (!sliceOk && stopOnError)
                {
                    sliceResults.Add(new
                    {
                        message = slice.Message,
                        path = slice.Path,
                        ok = false,
                        error = sliceError,
                        fix = fixResult,
                        steps = stepResults
                    });
                    break;
                }
            }

            for (var i = 0; i < slice.Steps.Count && i < MaxStepsPerSlice; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var step = slice.Steps[i];
                try
                {
                    var editArgs = BuildEditArgs(session, step, flush, diagnose);
                    var raw = await DocumentEditPlane.DispatchAsync(
                            "cdp_buffer", store, session, byDomain, editArgs, cancellationToken)
                        .ConfigureAwait(false);
                    stepResults.Add(new
                    {
                        index = i,
                        path = step.Path,
                        edit_op = step.EditOp,
                        ok = true,
                        result = JsonSerializer.Deserialize<JsonElement>(raw)
                    });
                }
                catch (Exception ex)
                {
                    sliceOk = false;
                    anyFail = true;
                    sliceError = ex.Message;
                    stepResults.Add(new
                    {
                        index = i,
                        path = step.Path,
                        edit_op = step.EditOp,
                        ok = false,
                        error = ex.Message
                    });
                    if (stopOnError)
                        break;
                }
            }

            sliceResults.Add(new
            {
                message = slice.Message,
                path = slice.Path,
                ok = sliceOk,
                error = sliceError,
                fix = fixResult,
                steps = stepResults
            });

            if (!sliceOk && stopOnError)
                break;
        }

        return JsonSerializer.Serialize(new
        {
            schema = PlanSchema,
            op = "apply",
            ok = !anyFail,
            stop_on_error = stopOnError,
            slice_count = sliceResults.Count,
            slices = sliceResults,
            next = anyFail
                ? "cdp_editor_scene + fix remaining slices"
                : "cdp_buffer op=diagnostics / cdp_build / git_git_scene"
        }, Pretty);
    }


}
