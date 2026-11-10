#nullable enable
using System.Text.Json;
using AIGuiders.Platform.Execution.CommandPlane;
using AIGuiders.Platform.Execution.CommandPlane.ArgSuggestions;
using AIGuiders.Platform.Execution.CommandPlane.Catalog;
using AIGuiders.Platform.Execution.Ide.Session;
using AIGuiders.Platform.IntermediateRepresentation.Command;
using AIGuiders.Platform.Modeling.CommandPlane;
using CommandPickerChoice = AIGuiders.Platform.Modeling.Gdl.Command.CommandPickerChoice;
using GdlCatalogRouteEntry = AIGuiders.Platform.Modeling.Gdl.Command.CatalogRouteEntry;
using Microsoft.FSharp.Core;
using AIGuiders.Platform.Modeling.Ide.Session;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Meta <c>cdp_suggest</c> — stateless attach step menu for agent seat (CDP-ADR-0229 ship-61b).
/// Wraps <see cref="FederationAttachSuggestions"/> brokers; generation vs choice — model picks id/n, not bracket DSL.
/// </summary>
internal static class CdpSuggestChannel
{
    public const string ToolName = "cdp_suggest";
    public const string Schema = "cdp_suggest/v0";

    /// <summary>Context budget (CDP-ADR-0229: the menu exists to STOP token spirals —
    /// it must not become one). More than MaxChoices matches → cap + truncated:true;
    /// an unfiltered catalog bigger than NarrowThreshold refuses instead of dumping.</summary>
    internal const int MaxChoices = 30;
    internal const int NarrowThreshold = 50;

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static readonly CommandCatalogIndex AttachCatalog =
        CommandCatalogIndex.FromDescriptors(FederationAttachCatalog.AllDescriptors());

    public static string HandleJson(SessionContext session, IReadOnlyDictionary<string, JsonElement> args) =>
        JsonSerializer.Serialize(Handle(session, args), Pretty);

    static object Handle(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var op = (Arg(args, "op") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "scene" or "map" or "catalog" => Scene(),
            "choices" or "pick" or "list" => Choices(session, args),
            _ => Fail("unknown_op", "op=scene|choices")
        };
    }

    static object Scene()
    {
        var verbs = AttachSchemaCatalog.schemas
            .Select(schema =>
            {
                var wire = AttachSchemaCatalog.verbWireName(schema.Verb);
                return new
                {
                    verb = wire,
                    target_case = schema.TargetCase,
                    steps = schema.Steps?
                        .Cast<AttachSchemaStep>()
                        .Select(step => new
                        {
                            id = step.Id,
                            prompt = step.Prompt,
                            suggestion_id = AttachSchemaModule.stepSuggestionId(step.Id)
                        })
                        .ToArray()
                };
            })
            .ToArray();

        return new
        {
            schema = Schema,
            ok = true,
            op = "scene",
            verb_suggestion_id = AttachSchemaCatalog.VerbSuggestionId,
            verbs,
            hint = "choices verb= (cold start) | step=pick_member path=foo.cs partial=Get — stateless each call; chain peek lines[].anchor first (ADR-0229)"
        };
    }

    static object Choices(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var partial = Arg(args, "partial") ?? Arg(args, "q") ?? Arg(args, "query") ?? "";
        var workspace = ResolveWorkspace(session, args);
        var path = Arg(args, "path") ?? Arg(args, "file");

        if (!TryResolveSuggestion(args, out var suggestionId, out var verb, out var resolveErr))
            return Fail("step_required", resolveErr ?? "pass step= or suggestion_id=");

        if (!TryResolveRoute(verb, suggestionId, out var route, out var canonicalPath, out resolveErr))
            return Fail("route_not_found", resolveErr ?? "unknown verb/step");

        var broker = FederationAttachSuggestions.CreateBroker(new CdpAttachSessionAccessor(workspace));
        var request = ArgSuggestionRequest.Create(
            suggestionId,
            partial,
            route,
            canonicalPath,
            workspace);

        var raw = broker.GetSuggestions(request);

        // path-aware picking: pick_member/pick_file scoped to the given file
        // (the broker catalogs the whole workspace; the file arg narrows it)
        if (path is { Length: > 0 })
        {
            var absolute = ResolveAgainstWorkspace(workspace, path);
            raw = raw
                .Where(choice =>
                {
                    var label = choice.LabelOrNull() ?? "";
                    return label.Contains(path, StringComparison.OrdinalIgnoreCase)
                        || label.Contains(absolute, StringComparison.OrdinalIgnoreCase);
                })
                .ToArray();
        }

        // budget gate: an unfiltered catalog refuses instead of flooding context
        if (raw.Count > NarrowThreshold && partial.Length == 0)
        {
            return Fail(
                "narrow_required",
                $"{raw.Count} candidates — pass partial= to narrow (e.g. partial=Card), or path= to scope to a file; " +
                "a full dump would burn the context the menu exists to save (CDP-ADR-0229).",
                raw.Count);
        }

        var truncated = raw.Count > MaxChoices;
        var choices = raw
            .Take(MaxChoices)
            .Select((choice, index) => MapChoice(workspace, index + 1, choice))
            .ToArray();

        return new
        {
            schema = Schema,
            ok = true,
            op = "choices",
            suggestion_id = suggestionId,
            verb,
            canonical_path = canonicalPath,
            partial,
            path,
            workspace,
            count = choices.Length,
            total = raw.Count,
            truncated,
            choices,
            hint = choices.Length > 0
                ? (truncated
                    ? $"Reply pick=n or value=; showing top {MaxChoices} of {raw.Count} — narrow with partial= to see the rest"
                    : "Reply pick=n or value=; re-call with verb/step/path/partial each turn — no server wizard state")
                : "No matches — widen partial= or cdp_open workspace for session-scoped pickers"
        };
    }

    static string ResolveAgainstWorkspace(string? workspace, string path)
    {
        if (Path.IsPathRooted(path) || workspace is null || workspace.Length == 0)
        {
            return path;
        }

        try
        {
            return Path.GetFullPath(Path.Combine(workspace, path));
        }
        catch
        {
            return path;
        }
    }

    static object MapChoice(string? workspace, int n, CommandPickerChoice choice)
    {
        // compact labels: candidate labels carry the full absolute path
        // («Value — D:/ws/src/…») — a known workspace makes the relative path enough.
        // The workspace anchor may be the .slnx FILE (labels lead from its directory),
        // and slash direction varies — try both roots, both separators.
        var label = choice.LabelOrNull();
        if (label is { Length: > 0 } && workspace is { Length: > 0 })
        {
            var roots = new List<string> { workspace };
            try
            {
                var dir = Path.GetDirectoryName(workspace);
                if (dir is { Length: > 0 })
                {
                    roots.Add(dir);
                }
            }
            catch
            {
                // not a rooted workspace path — nothing to strip
            }

            foreach (var root in roots)
            {
                var normalized = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                // slash direction varies between the session anchor and broker labels
                foreach (var variant in new[] { normalized.Replace('\\', '/'), normalized.Replace('/', '\\') })
                {
                    label = label.Replace(variant + "/", "", StringComparison.OrdinalIgnoreCase)
                                 .Replace(variant + "\\", "", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        return new
        {
            n,
            value = choice.Value,
            label,
            hint = choice.HintOrNull()
        };
    }

    static bool TryResolveSuggestion(
        IReadOnlyDictionary<string, JsonElement> args,
        out string suggestionId,
        out string? verb,
        out string? error)
    {
        suggestionId = "";
        verb = NormalizeVerb(Arg(args, "verb"));
        var step = Arg(args, "step") ?? Arg(args, "step_id");
        var explicitId = Arg(args, "suggestion_id") ?? Arg(args, "suggestion");

        if (explicitId is { Length: > 0 })
        {
            suggestionId = explicitId.Trim();
            if (verb is null)
            {
                var entry = AttachSchemaCatalog.tryFindStepBySuggestionId(suggestionId);
                if (FSharpOption<AttachSchemaStepEntry>.get_IsSome(entry))
                    verb = AttachSchemaCatalog.verbWireName(entry.Value.Verb);
            }

            error = null;
            return true;
        }

        if (step is { Length: > 0 })
        {
            suggestionId = AttachSchemaModule.stepSuggestionId(step.Trim());
            if (verb is null)
            {
                var entry = AttachSchemaCatalog.tryFindStepBySuggestionId(suggestionId);
                if (FSharpOption<AttachSchemaStepEntry>.get_IsSome(entry))
                    verb = AttachSchemaCatalog.verbWireName(entry.Value.Verb);
            }

            error = null;
            return true;
        }

        if (verb is { Length: > 0 })
        {
            var parsedVerbOpt = AttachSchemaModule.tryParseVerbWire(verb);
            if (FSharpOption<AttachVerb>.get_IsSome(parsedVerbOpt))
            {
                var parsedVerb = parsedVerbOpt.Value;
                var schema = AttachSchemaModule.forVerb(parsedVerb);
                var firstStep = schema.Steps?.Cast<AttachSchemaStep>().FirstOrDefault();
                if (firstStep is null)
                {
                    error = $"attach verb \"{verb}\" has no steps";
                    return false;
                }

                suggestionId = AttachSchemaModule.stepSuggestionId(firstStep.Id);
                verb = AttachSchemaCatalog.verbWireName(schema.Verb);
                error = null;
                return true;
            }
        }

        suggestionId = AttachSchemaCatalog.VerbSuggestionId;
        error = null;
        return true;
    }

    static bool TryResolveRoute(
        string? verb,
        string suggestionId,
        out GdlCatalogRouteEntry route,
        out string canonicalPath,
        out string? error)
    {
        route = default!;
        canonicalPath = "";
        error = null;

        if (string.Equals(suggestionId, AttachSchemaCatalog.VerbSuggestionId, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(verb))
        {
            if (!AttachCatalog.TryGet("attach", out route))
            {
                error = "attach root missing from catalog";
                return false;
            }

            canonicalPath = "attach";
            return true;
        }

        var path = $"attach {verb!.Trim()}";
        if (!AttachCatalog.TryGet(path, out route))
        {
            error = $"verb \"{verb}\" not in attach catalog";
            return false;
        }

        canonicalPath = path;
        return true;
    }

    static string? ResolveWorkspace(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var explicitWs = Arg(args, "workspace") ?? Arg(args, "workspace_anchor");
        if (explicitWs is { Length: > 0 })
            return explicitWs.Trim();

        return session.SolutionOrProjectPath ?? session.ProjectRoot;
    }

    static string? NormalizeVerb(string? verb) =>
        string.IsNullOrWhiteSpace(verb) ? null : verb.Trim();

    sealed class CdpAttachSessionAccessor(string? pinnedWorkspace) : IAttachSessionAccessor
    {
        public SessionRuntime? TryGet(string? workspaceAnchor) =>
            FederationAttachSessionAccessor.Instance.TryGet(pinnedWorkspace ?? workspaceAnchor);
    }

    static string? Arg(IReadOnlyDictionary<string, JsonElement> args, string name) =>
        args.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    static object Fail(string code, string why) => new
    {
        schema = Schema,
        ok = false,
        error = code,
        why
    };

    static object Fail(string code, string why, int total) => new
    {
        schema = Schema,
        ok = false,
        error = code,
        why,
        total
    };
}
