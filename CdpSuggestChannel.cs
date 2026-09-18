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

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
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
        var choices = raw.Select((choice, index) => MapChoice(index + 1, choice)).ToArray();

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
            choices,
            hint = choices.Length > 0
                ? "Reply pick=n or value=; re-call with verb/step/path/partial each turn — no server wizard state"
                : "No matches — widen partial= or cdp_open workspace for session-scoped pickers"
        };
    }

    static object MapChoice(int n, CommandPickerChoice choice) => new
    {
        n,
        value = choice.Value,
        label = choice.LabelOrNull(),
        hint = choice.HintOrNull()
    };

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
}
