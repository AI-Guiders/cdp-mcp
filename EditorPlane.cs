using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// Editor plane — git_scene/git_plan isomorphism for buffers (kj-20260724-1640).
/// <c>cdp_editor_scene</c> maps open buffers + optional context; <c>cdp_edit_plan</c>
/// drafts candidates then validate|apply logical slices of buffer edits.
/// </summary>
internal static class EditorPlane
{
    public const string SceneSchema = "editor_scene/v0";
    public const string PlanSchema = "edit_plan/v0";
    public const int MaxSlices = 32;
    public const int MaxStepsPerSlice = 64;
    public const int ContextMaxLinesDefault = 80;

    public const string ExampleYaml =
        """
        - message: why this logical edit group
          steps:
            - path: Foo.cs
              edit_op: replace
              old_string: old
              new_string: new
            # or: edit_op: anchor / anchor: "[F:Foo.cs;M:Bar;K:Method]" / text: "…"
        """;

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly YamlDotNet.Serialization.IDeserializer Yaml =
        new YamlDotNet.Serialization.DeserializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

    public static bool IsEditorTool(string name) =>
        name is "cdp_editor_scene" or "cdp_edit_plan" || EditSniper.IsSniperTool(name);

    public static async Task<string> DispatchAsync(
        string name,
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken) =>
        name switch
        {
            "cdp_editor_scene" => Scene(store, session, args),
            "cdp_edit_plan" => await PlanAsync(store, session, byDomain, args, cancellationToken)
                .ConfigureAwait(false),
            _ when EditSniper.IsSniperTool(name) => EditSniper.Dispatch(store, session, args),
            _ => throw new ArgumentException($"Unknown editor tool: {name}")
        };

    static string Scene(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var docs = store.All
            .OrderBy(b => b.DocId, StringComparer.Ordinal)
            .ToArray();

        var loci = docs.Select(b => new
        {
            id = $"buffer:{b.DocId}",
            kind = "buffer",
            pulse = (b.Dirty ? "DIRTY " : "") + ShortPath(b.Path),
            drill = "cdp_editor_scene path=… | cdp_edit_plan",
            doc_id = b.DocId,
            path = b.Path,
            dirty = b.Dirty,
            language = b.Language,
            version = b.Version,
            line_count = CountLines(b.Text),
            diags_cached = b.LastDiagnosedVersion == b.Version && b.LastDiagnosticsJson is { Length: > 0 }
        }).Cast<object>().ToList();

        if (docs.Length == 0)
        {
            loci.Add(new
            {
                id = "buffer:none",
                kind = "buffer",
                pulse = "no open buffers",
                drill = "cdp_buffer op=open",
                count = 0
            });
        }

        object? context = null;
        var focusPath = OptString(args, "path");
        var focusLocus = OptString(args, "locus") ?? OptString(args, "focus");
        var focusDocId = OptString(args, "doc_id");

        if (focusLocus is { Length: > 0 }
            && focusLocus.StartsWith("buffer:", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(focusLocus, "buffer:none", StringComparison.OrdinalIgnoreCase))
        {
            focusDocId ??= focusLocus["buffer:".Length..];
        }

        if (focusPath is { Length: > 0 } || focusDocId is { Length: > 0 })
        {
            try
            {
                var resolved = focusPath is { Length: > 0 } ? ResolveUserPath(session, focusPath) : null;
                var buf = store.Resolve(resolved, focusDocId);
                var maxLines = Math.Clamp(IntOr(args, "context_lines", ContextMaxLinesDefault), 1, 400);
                var start = IntOrNull(args, "start_line") ?? 1;
                var end = IntOrNull(args, "end_line") ?? Math.Min(CountLines(buf.Text), start + maxLines - 1);
                context = new
                {
                    ok = true,
                    locus = $"buffer:{buf.DocId}",
                    meta = buf.ToMeta(),
                    window = buf.ToReadResult(start, end),
                    diags_note = buf.LastDiagnosedVersion == buf.Version
                        ? "cache_available (cdp_buffer op=diagnostics)"
                        : "stale_or_missing — run cdp_buffer op=diagnostics"
                };
            }
            catch (Exception ex)
            {
                context = new
                {
                    ok = false,
                    path = focusPath,
                    doc_id = focusDocId,
                    locus = focusLocus,
                    error = ex.Message,
                    hint = "Open first: cdp_buffer op=open path=…"
                };
            }
        }

        return JsonSerializer.Serialize(new
        {
            schema = SceneSchema,
            ok = true,
            session = new
            {
                project_root = session.ProjectRoot,
                language = session.Language,
                solution_or_project_path = session.SolutionOrProjectPath
            },
            count = docs.Length,
            dirty_count = docs.Count(d => d.Dirty),
            buffers = docs.Select(b => b.ToMeta()).ToArray(),
            loci,
            context,
            next = new
            {
                draft = "cdp_edit_plan op=draft",
                apply = "cdp_edit_plan op=apply yaml=… (preferred) or slices=[]",
                buffer = "cdp_buffer still fine for single surgical edit"
            },
            hint =
                "Map first (this tool); multi-step → edit_plan YAML slices (git_plan analogue). " +
                "Prefer edit_op=anchor [F:;M:;K:]. path=/locus= for context on demand — not a full dump."
        }, Pretty);
    }

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

    sealed class EditSlice(string Message, IReadOnlyList<EditStep> Steps, string? Path = null, IReadOnlyList<string>? FixIds = null)
    {
        public string Message { get; } = Message;
        public string? Path { get; } = Path;
        public IReadOnlyList<string> FixIds { get; } = FixIds ?? [];
        public IReadOnlyList<EditStep> Steps { get; } = Steps;
    }

    sealed class EditStep
    {
        public string? Path { get; init; }
        public string? EditOp { get; init; }
        public string? Anchor { get; init; }
        public string? At { get; init; }
        public string? Text { get; init; }
        public string? OldString { get; init; }
        public string? NewString { get; init; }
        public int? StartLine { get; init; }
        public int? StartColumn { get; init; }
        public int? EndLine { get; init; }
        public int? EndColumn { get; init; }
        public bool? AllowShrink { get; init; }
    }

    static IReadOnlyList<EditSlice>? TryGetSlices(IReadOnlyDictionary<string, JsonElement> args)
    {
        // Prefer YAML string: yaml= | slices_yaml= | plan=
        foreach (var key in new[] { "yaml", "slices_yaml", "plan" })
        {
            var y = OptString(args, key);
            if (y is { Length: > 0 })
                return ParseYamlSlices(y);
        }

        if (!args.TryGetValue("slices", out var el))
            return null;

        // slices as YAML/JSON string (agents often paste a block)
        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (string.IsNullOrWhiteSpace(s))
                return null;
            var t = s.TrimStart();
            if (t.StartsWith('[') || t.StartsWith('{'))
                return ParseJsonSlices(s);
            return ParseYamlSlices(s);
        }

        if (el.ValueKind != JsonValueKind.Array)
            return null;

        return ParseJsonArray(el);
    }

    static IReadOnlyList<EditSlice> ParseYamlSlices(string yaml)
    {
        try
        {
            var trimmed = yaml.TrimStart();
            // Document form: path + fix: (and optional steps/slices wrapper)
            if (trimmed.StartsWith("path:", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("fix:", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("slices:", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("---", StringComparison.Ordinal))
            {
                var wrap = Yaml.Deserialize<YamlPlanDoc>(yaml);
                if (wrap is not null)
                {
                    if (wrap.Slices is { Count: > 0 })
                        return wrap.Slices.Select(FromYamlSlice).ToArray();
                    if ((wrap.Fix is { Count: > 0 } || wrap.Steps is { Count: > 0 })
                        && !string.IsNullOrWhiteSpace(wrap.Path))
                    {
                        return
                        [
                            new EditSlice(
                                wrap.Message ?? "",
                                (wrap.Steps ?? []).Select(FromYamlStep).ToArray(),
                                wrap.Path,
                                wrap.Fix ?? [])
                        ];
                    }
                }
            }

            var list = Yaml.Deserialize<List<YamlSliceDto>>(yaml);
            if (list is null || list.Count == 0)
                throw new ArgumentException("YAML slices empty.");
            return list.Select(FromYamlSlice).ToArray();
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new ArgumentException($"YAML slices parse failed: {ex.Message}");
        }
    }

    static EditSlice FromYamlSlice(YamlSliceDto s)
    {
        var steps = (s.Steps ?? []).Select(FromYamlStep).ToArray();
        // If steps omit path but slice has path, inherit for mutate steps.
        if (!string.IsNullOrWhiteSpace(s.Path))
        {
            steps = steps.Select(st => string.IsNullOrWhiteSpace(st.Path)
                ? new EditStep
                {
                    Path = s.Path,
                    EditOp = st.EditOp,
                    Anchor = st.Anchor,
                    At = st.At,
                    Text = st.Text,
                    OldString = st.OldString,
                    NewString = st.NewString,
                    StartLine = st.StartLine,
                    StartColumn = st.StartColumn,
                    EndLine = st.EndLine,
                    EndColumn = st.EndColumn,
                    AllowShrink = st.AllowShrink
                }
                : st).ToArray();
        }

        return new EditSlice(s.Message ?? "", steps, s.Path, s.Fix ?? []);
    }

    static EditStep FromYamlStep(YamlStepDto st) => new()
    {
        Path = st.Path,
        EditOp = st.EditOp ?? st.Op,
        Anchor = st.Anchor,
        At = st.At,
        Text = st.Text,
        OldString = st.OldString,
        NewString = st.NewString,
        StartLine = st.StartLine,
        StartColumn = st.StartColumn,
        EndLine = st.EndLine,
        EndColumn = st.EndColumn,
        AllowShrink = st.AllowShrink
    };

    static IReadOnlyList<EditSlice> ParseJsonSlices(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("slices", out var inner))
            return ParseJsonArray(inner);
        if (root.ValueKind == JsonValueKind.Array)
            return ParseJsonArray(root);
        throw new ArgumentException("JSON slices must be an array or {slices:[…]}.");
    }

    static IReadOnlyList<EditSlice> ParseJsonArray(JsonElement el)
    {
        var list = new List<EditSlice>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var message = item.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            var path = item.TryGetProperty("path", out var p) ? p.GetString() : null;
            var fixIds = new List<string>();
            if (item.TryGetProperty("fix", out var fixEl) && fixEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in fixEl.EnumerateArray())
                {
                    if (f.ValueKind == JsonValueKind.String && f.GetString() is { Length: > 0 } id)
                        fixIds.Add(id);
                    else if (f.ValueKind == JsonValueKind.Object && f.TryGetProperty("id", out var idEl)
                             && idEl.GetString() is { Length: > 0 } oid)
                        fixIds.Add(oid);
                }
            }

            var steps = new List<EditStep>();
            if (item.TryGetProperty("steps", out var stepsEl) && stepsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in stepsEl.EnumerateArray())
                {
                    if (s.ValueKind != JsonValueKind.Object)
                        continue;
                    steps.Add(ParseStep(s));
                }
            }

            list.Add(new EditSlice(message, steps, path, fixIds));
        }

        return list;
    }

    sealed class YamlPlanDoc
    {
        public string? Path { get; set; }
        public string? Message { get; set; }
        public List<string>? Fix { get; set; }
        public List<YamlStepDto>? Steps { get; set; }
        public List<YamlSliceDto>? Slices { get; set; }
    }

    sealed class YamlSliceDto
    {
        public string? Path { get; set; }
        public string? Message { get; set; }
        public List<string>? Fix { get; set; }
        public List<YamlStepDto>? Steps { get; set; }
    }

    sealed class YamlStepDto
    {
        public string? Path { get; set; }
        public string? EditOp { get; set; }
        public string? Op { get; set; }
        public string? Anchor { get; set; }
        public string? At { get; set; }
        public string? Text { get; set; }
        public string? OldString { get; set; }
        public string? NewString { get; set; }
        public int? StartLine { get; set; }
        public int? StartColumn { get; set; }
        public int? EndLine { get; set; }
        public int? EndColumn { get; set; }
        public bool? AllowShrink { get; set; }
    }

    static EditStep ParseStep(JsonElement s) => new()
    {
        Path = PropString(s, "path"),
        EditOp = PropString(s, "edit_op") ?? PropString(s, "op"),
        Anchor = PropString(s, "anchor"),
        At = PropString(s, "at"),
        Text = PropString(s, "text"),
        OldString = PropString(s, "old_string"),
        NewString = PropString(s, "new_string"),
        StartLine = PropInt(s, "start_line"),
        StartColumn = PropInt(s, "start_column"),
        EndLine = PropInt(s, "end_line"),
        EndColumn = PropInt(s, "end_column"),
        AllowShrink = PropBool(s, "allow_shrink")
    };

    static string? PropString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    static int? PropInt(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.TryGetInt32(out var n) ? n : null;

    static bool? PropBool(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return null;
        return p.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    static IReadOnlyList<string> StringArray(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.Array)
            return [];
        return el.EnumerateArray()
            .Select(e => e.GetString())
            .Where(s => s is { Length: > 0 })
            .Cast<string>()
            .ToArray();
    }

    static bool PathMatches(string full, string pattern)
    {
        if (string.Equals(full, pattern, StringComparison.OrdinalIgnoreCase))
            return true;
        if (full.EndsWith(pattern.Replace('/', Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            return true;
        return full.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    static string ResolveUserPath(SessionContext session, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var p = path.Trim();
        if (Path.IsPathRooted(p))
            return Path.GetFullPath(p);

        var root = session.ProjectRoot is { Length: > 0 } pr
            ? pr
            : Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(root, p));
    }

    static string ShortPath(string path)
    {
        if (path.Length <= 64) return path;
        var name = Path.GetFileName(path);
        var dir = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
        return string.IsNullOrEmpty(dir) ? "…/" + name : $"…/{dir}/{name}";
    }

    static int CountLines(string text)
    {
        if (text.Length == 0) return 1;
        var n = 1;
        foreach (var ch in text)
        {
            if (ch == '\n') n++;
        }

        return n;
    }

    static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) ? el.GetString() : null;

    static int IntOr(IReadOnlyDictionary<string, JsonElement> args, string key, int defaultValue) =>
        args.TryGetValue(key, out var el) && el.TryGetInt32(out var n) ? n : defaultValue;

    static int? IntOrNull(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.TryGetInt32(out var n) ? n : null;

    static bool BoolOr(IReadOnlyDictionary<string, JsonElement> args, string key, bool defaultValue)
    {
        if (!args.TryGetValue(key, out var el))
            return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }
}
