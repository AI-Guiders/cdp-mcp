using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using CdpMcp.Cockpit.Surface;

namespace CdpMcp;

internal static partial class EditorPlane
{
    static async Task<string> PlanAsync(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var op = (OptString(args, "op") ?? "draft").Trim().ToLowerInvariant();
        return op switch
        {
            "draft" => await DraftAsync(store, session, byDomain, args, cancellationToken)
                .ConfigureAwait(false),
            "validate" => await ValidateAsync(store, session, byDomain, args, cancellationToken)
                .ConfigureAwait(false),
            "apply" => await ApplyAsync(store, session, byDomain, args, cancellationToken)
                .ConfigureAwait(false),
            "preview" => await ValidateAsync(store, session, byDomain, args, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new ArgumentException("cdp_edit_plan op must be draft|validate|apply (preview→validate).")
        };
    }

    static async Task<string> DraftAsync(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var docs = store.All
            .OrderBy(b => b.DocId, StringComparer.Ordinal)
            .ToArray();

        var include = StringArray(args, "include");
        var sketch = (OptString(args, "sketch") ?? "").Trim().ToLowerInvariant();
        var candidates = new List<object>();
        foreach (var b in docs)
        {
            if (include.Count > 0
                && !include.Any(p => PathMatches(b.Path, p) || string.Equals(b.DocId, p, StringComparison.OrdinalIgnoreCase)))
                continue;

            candidates.Add(new
            {
                path = b.Path,
                doc_id = b.DocId,
                dirty = b.Dirty,
                language = b.Language,
                version = b.Version,
                line_count = CountLines(b.Text),
                preferred_edit_op = string.Equals(b.Language, "csharp", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(b.Language, "xml", StringComparison.OrdinalIgnoreCase)
                    ? "anchor"
                    : "replace"
            });
        }

        foreach (var raw in include)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("doc-", StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                var full = ResolveUserPath(session, raw);
                if (docs.Any(d => string.Equals(d.Path, full, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (!File.Exists(full))
                {
                    candidates.Add(new { path = full, ok = false, error = "path_not_found" });
                    continue;
                }

                candidates.Add(new
                {
                    path = full,
                    dirty = false,
                    language = DocumentBufferStore.GuessLanguage(full),
                    opened = false,
                    preferred_edit_op = "anchor",
                    hint = "cdp_buffer op=open before apply (or apply will open)"
                });
            }
            catch (Exception ex)
            {
                candidates.Add(new { path = raw, ok = false, error = ex.Message });
            }
        }

        string? suggestedYaml = null;
        if (sketch is "fix" or "diags" or "diagnostics")
        {
            var focus = OptString(args, "path");
            string? target = null;
            if (focus is { Length: > 0 })
                target = ResolveUserPath(session, focus);
            else if (docs.FirstOrDefault(d =>
                         string.Equals(d.Language, "csharp", StringComparison.OrdinalIgnoreCase)) is { } cs)
                target = cs.Path;
            else if (docs.Length > 0)
                target = docs[0].Path;

            if (target is not null)
                suggestedYaml = await EditPlanFix.SuggestFixYamlAsync(session, byDomain, target, cancellationToken)
                    .ConfigureAwait(false);
        }

        return JsonSerializer.Serialize(new
        {
            schema = PlanSchema,
            op = "draft",
            ok = true,
            sketch = string.IsNullOrEmpty(sketch) ? null : sketch,
            hint =
                "Mutate: yaml steps. Fix (code action): yaml with path+fix:[IDE0005,…]. " +
                "sketch=fix → suggested_yaml from document diags. Prefer stable diagnostic ids.",
            session = new { project_root = session.ProjectRoot, language = session.Language },
            candidates,
            candidate_count = candidates.Count,
            suggested_yaml = suggestedYaml,
            example_yaml = ExampleYaml,
            example_fix_yaml =
                """
                - path: Foo.cs
                  message: clean document
                  fix:
                    - IDE0005
                """
        }, Pretty);
    }

    static async Task<string> ValidateAsync(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var slices = TryGetSlices(args)
            ?? throw new ArgumentException(
                "cdp_edit_plan validate requires yaml= (YAML list) or slices=[{message,steps|fix:…}].");
        var resolveAnchors = BoolOr(args, "resolve_anchors", defaultValue: true);
        var results = new List<object>();
        var anyFail = false;

        foreach (var slice in slices.Take(MaxSlices))
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(slice.Message) && slice.FixIds.Count == 0 && slice.Steps.Count == 0)
                errors.Add("message or fix/steps required");
            if (slice.FixIds.Count == 0 && slice.Steps.Count == 0)
                errors.Add("fix and/or steps required");
            if (slice.Steps.Count > MaxStepsPerSlice)
                errors.Add($"steps_cap:{MaxStepsPerSlice}");

            object? fixCheck = null;
            if (slice.FixIds.Count > 0)
            {
                var path = slice.Path;
                if (string.IsNullOrWhiteSpace(path))
                    errors.Add("path required for fix:");
                else
                {
                    var full = ResolveUserPath(session, path);
                    var (ok, err, hits) = await EditPlanFix.FindHitsAsync(
                            session, byDomain, full, slice.FixIds, cancellationToken)
                        .ConfigureAwait(false);
                    if (!ok)
                        errors.Add(err ?? "fix_validate_failed");
                    fixCheck = new
                    {
                        ok,
                        path = full,
                        ids = slice.FixIds,
                        hits = hits.Select(h => new { h.Id, h.Line, h.Column, h.Message }).ToArray(),
                        error = err
                    };
                }
            }

            var stepResults = new List<object>();
            for (var i = 0; i < slice.Steps.Count && i < MaxStepsPerSlice; i++)
            {
                var step = slice.Steps[i];
                var stepErrors = ValidateStep(store, session, step, resolveAnchors);
                if (stepErrors.Count > 0)
                    errors.AddRange(stepErrors.Select(e => $"step[{i}]:{e}"));
                stepResults.Add(new
                {
                    index = i,
                    path = step.Path,
                    edit_op = step.EditOp,
                    ok = stepErrors.Count == 0,
                    errors = stepErrors
                });
            }

            var okSlice = errors.Count == 0;
            if (!okSlice) anyFail = true;
            results.Add(new
            {
                message = slice.Message,
                path = slice.Path,
                ok = okSlice,
                fix = fixCheck,
                step_count = slice.Steps.Count,
                errors,
                steps = stepResults
            });
        }

        return JsonSerializer.Serialize(new
        {
            schema = PlanSchema,
            op = "validate",
            ok = !anyFail,
            slice_count = results.Count,
            slices = results,
            hint = anyFail
                ? "Fix slice errors before op=apply."
                : "Ready: cdp_edit_plan op=apply with the same yaml."
        }, Pretty);
    }

}
