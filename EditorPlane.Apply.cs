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

    static List<string> ValidateStep(
        DocumentBufferStore store,
        SessionContext session,
        EditStep step,
        bool resolveAnchors)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(step.Path))
            errors.Add("path required");
        if (string.IsNullOrWhiteSpace(step.EditOp))
            errors.Add("edit_op required");

        var op = (step.EditOp ?? "").Trim().ToLowerInvariant();
        if (op is not ("anchor" or "replace" or "replace_range" or "set_text"))
            errors.Add("edit_op must be anchor|replace|replace_range|set_text");

        string? fullPath = null;
        if (!string.IsNullOrWhiteSpace(step.Path))
        {
            try
            {
                fullPath = ResolveUserPath(session, step.Path);
                var open = store.All.FirstOrDefault(b =>
                    string.Equals(b.Path, fullPath, StringComparison.OrdinalIgnoreCase));
                if (open is null && !File.Exists(fullPath))
                    errors.Add($"path_not_found:{fullPath}");
            }
            catch (Exception ex)
            {
                errors.Add($"path_resolve:{ex.Message}");
            }
        }

        switch (op)
        {
            case "anchor":
                if (string.IsNullOrWhiteSpace(step.Anchor) && string.IsNullOrWhiteSpace(step.At))
                    errors.Add("anchor (or at) required");
                if (string.IsNullOrWhiteSpace(step.Text) && string.IsNullOrWhiteSpace(step.NewString))
                    errors.Add("text (or new_string) required");
                if (resolveAnchors
                    && errors.Count == 0
                    && fullPath is not null
                    && (File.Exists(fullPath)
                        || store.All.Any(b => string.Equals(b.Path, fullPath, StringComparison.OrdinalIgnoreCase))))
                {
                    var wire = step.Anchor ?? step.At!;
                    try
                    {
                        var span = BracketLocate.Parse(wire);
                        var text = store.All
                            .FirstOrDefault(b => string.Equals(b.Path, fullPath, StringComparison.OrdinalIgnoreCase))
                            ?.Text
                            ?? File.ReadAllText(fullPath);
                        var family = BracketLocate.ClassifyFamily(span, out var familyError);
                        if (familyError is not null)
                            errors.Add(familyError);
                        else if (family == BracketLocate.AxisFamily.Csharp)
                        {
                            if (!BracketSyntaxResolve.TryResolve(fullPath, text, span, out _, out var detail))
                                errors.Add($"anchor_resolve:{detail}");
                        }
                        else if (family == BracketLocate.AxisFamily.Xml)
                        {
                            if (!BracketXmlResolve.TryResolve(fullPath, text, span, out _, out var detail))
                                errors.Add($"anchor_resolve:{detail}");
                        }
                        else
                            errors.Add("anchor_family_none");
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"anchor_parse:{ex.Message}");
                    }
                }

                break;
            case "replace":
                if (string.IsNullOrWhiteSpace(step.OldString))
                    errors.Add("old_string required");
                break;
            case "replace_range":
                if (step.StartLine is null || step.StartColumn is null
                    || step.EndLine is null || step.EndColumn is null)
                    errors.Add("start_line/start_column/end_line/end_column required");
                break;
            case "set_text":
                // text may be empty intentionally
                break;
        }

        return errors;
    }

    static Dictionary<string, JsonElement> BuildEditArgs(
        SessionContext session,
        EditStep step,
        bool flush,
        bool diagnose)
    {
        var path = ResolveUserPath(session, step.Path!);
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("edit"),
            ["path"] = JsonSerializer.SerializeToElement(path),
            ["edit_op"] = JsonSerializer.SerializeToElement(step.EditOp!.Trim().ToLowerInvariant()),
            ["flush"] = JsonSerializer.SerializeToElement(flush),
            ["diagnose"] = JsonSerializer.SerializeToElement(diagnose)
        };

        void Put(string key, string? value)
        {
            if (value is not null)
                dict[key] = JsonSerializer.SerializeToElement(value);
        }

        void PutInt(string key, int? value)
        {
            if (value is not null)
                dict[key] = JsonSerializer.SerializeToElement(value.Value);
        }

        Put("anchor", step.Anchor);
        Put("at", step.At);
        Put("text", step.Text);
        Put("old_string", step.OldString);
        Put("new_string", step.NewString);
        PutInt("start_line", step.StartLine);
        PutInt("start_column", step.StartColumn);
        PutInt("end_line", step.EndLine);
        PutInt("end_column", step.EndColumn);
        if (step.AllowShrink is { } ash)
            dict["allow_shrink"] = JsonSerializer.SerializeToElement(ash);

        return dict;
    }

}
