using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using CdpMcp.Cockpit.Surface;

namespace CdpMcp;

/// <summary>
/// Editor plane — git_scene/git_plan isomorphism for buffers (kj-20260724-1640).
/// <c>cdp_editor_scene</c> defaults to desk-parity pulse; <c>detail=full</c> / path|locus|doc_id
/// maps open buffers + optional context; <c>cdp_edit_plan</c>
/// drafts candidates then validate|apply logical slices of buffer edits.
/// </summary>
internal static partial class EditorPlane
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
        var focusPath = OptString(args, "path");
        var focusLocus = OptString(args, "locus") ?? OptString(args, "focus");
        var focusDocId = OptString(args, "doc_id");
        var detail = (OptString(args, "detail") ?? "pulse").Trim().ToLowerInvariant();

        if (focusLocus is { Length: > 0 }
            && focusLocus.StartsWith("buffer:", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(focusLocus, "buffer:none", StringComparison.OrdinalIgnoreCase))
        {
            focusDocId ??= focusLocus["buffer:".Length..];
        }

        var wantsFull = detail is "full" or "map"
            || focusPath is { Length: > 0 }
            || focusDocId is { Length: > 0 };

        if (!wantsFull)
            return ScenePulse(store, session);

        return SceneFull(store, session, args, focusPath, focusLocus, focusDocId);
    }


}
