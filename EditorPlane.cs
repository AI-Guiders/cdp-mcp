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

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static bool IsEditorTool(string name) =>
        name is "cdp_editor_scene" or "cdp_edit_plan";

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
                apply = "cdp_edit_plan op=apply slices=[{message,steps:[{path,edit_op,…}]}]",
                buffer = "cdp_buffer still fine for single surgical edit"
            },
            hint =
                "Map first (this tool); multi-step → edit_plan slices (git_plan analogue). " +
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
            "draft" => Draft(store, session, args),
            "validate" => Validate(store, session, args),
            "apply" => await ApplyAsync(store, session, byDomain, args, cancellationToken)
                .ConfigureAwait(false),
            "preview" => Validate(store, session, args), // alias like test_plan
            _ => throw new ArgumentException("cdp_edit_plan op must be draft|validate|apply (preview→validate).")
        };
    }

    static string Draft(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var docs = store.All
            .OrderBy(b => b.DocId, StringComparer.Ordinal)
            .ToArray();

        var include = StringArray(args, "include");
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

        // Optional disk paths not yet opened — still list as cold candidates.
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

        return JsonSerializer.Serialize(new
        {
            schema = PlanSchema,
            op = "draft",
            ok = true,
            hint =
                "Split work into slices[{message, steps:[{path, edit_op, …}]}]; then op=validate or op=apply. " +
                "edit_op=anchor|replace|replace_range|set_text. Prefer anchor [F:;M:;K:].",
            session = new { project_root = session.ProjectRoot, language = session.Language },
            candidates,
            candidate_count = candidates.Count,
            example_slice = new
            {
                message = "why this logical edit group",
                steps = new object[]
                {
                    new
                    {
                        path = "Foo.cs",
                        edit_op = "anchor",
                        anchor = "[F:Foo.cs;M:Bar;K:Method]",
                        text = "// replacement body"
                    }
                }
            }
        }, Pretty);
    }

    static string Validate(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var slices = TryGetSlices(args)
            ?? throw new ArgumentException(
                "cdp_edit_plan validate requires slices=[{message,steps:[{path,edit_op,…}]}].");
        var resolveAnchors = BoolOr(args, "resolve_anchors", defaultValue: true);
        var results = new List<object>();
        var anyFail = false;

        foreach (var slice in slices.Take(MaxSlices))
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(slice.Message))
                errors.Add("message required");
            if (slice.Steps.Count == 0)
                errors.Add("steps required (non-empty)");
            if (slice.Steps.Count > MaxStepsPerSlice)
                errors.Add($"steps_cap:{MaxStepsPerSlice}");

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

            var ok = errors.Count == 0;
            if (!ok) anyFail = true;
            results.Add(new
            {
                message = slice.Message,
                ok,
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
                : "Ready: cdp_edit_plan op=apply with the same slices."
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
                "cdp_edit_plan apply requires slices=[{message,steps:[{path,edit_op,…}]}].");
        var stopOnError = BoolOr(args, "stop_on_error", defaultValue: true);
        var diagnose = BoolOr(args, "diagnose", defaultValue: true);
        var flush = BoolOr(args, "flush", defaultValue: true);
        var skipValidate = BoolOr(args, "skip_validate", defaultValue: false);

        if (!skipValidate)
        {
            var pre = Validate(store, session, args);
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
            var sliceOk = true;
            string? sliceError = null;

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
                ok = sliceOk,
                error = sliceError,
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

    sealed class EditSlice(string Message, IReadOnlyList<EditStep> Steps)
    {
        public string Message { get; } = Message;
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
        if (!args.TryGetValue("slices", out var el) || el.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<EditSlice>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var message = item.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
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

            list.Add(new EditSlice(message, steps));
        }

        return list;
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
