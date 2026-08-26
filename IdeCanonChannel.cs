using System.Text.Json;
using System.Text.Json.Nodes;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

internal static class IdeCanonChannel
{
    internal sealed record CanonStackWire(
        string ScmRoot,
        string SettingsPath,
        string SettingsSource,
        string? EffectiveLang,
        string LangSource,
        Dictionary<string, object> CanonStack);

    internal static string HandleJson(
        SessionContext session,
        CdpSettings cdpSettings,
        DocumentBufferStore docStore,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var wire = TryBuildStack(session, cdpSettings, docStore);
        if (wire is null)
            return JsonSerializer.Serialize(new
            {
                ok = false,
                error = "need_scm_root",
                hint = "cdp_open first — canon stack needs session scm_root.",
            });

        return JsonSerializer.Serialize(new
        {
            ok = true,
            scm_root = wire.ScmRoot,
            settings_path = wire.SettingsPath,
            settings_source = wire.SettingsSource,
            lang = wire.EffectiveLang,
            lang_source = wire.LangSource,
            host_paths = BuildHostPaths(session, cdpSettings, docStore),
            canon_stack = wire.CanonStack,
        });
    }

  /// <summary>Embed slim canon route in <c>cdp_open</c> / restore JSON (CDP-ADR-0207).</summary>
    internal static string AttachCanonToOpenJson(
        string openJson,
        SessionContext session,
        CdpSettings cdpSettings,
        DocumentBufferStore docStore)
    {
        var wire = TryBuildStack(session, cdpSettings, docStore);
        if (wire is null)
            return openJson;

        try
        {
            var node = JsonNode.Parse(openJson)?.AsObject();
            if (node is null)
                return openJson;

            node["canon_lang"] = wire.EffectiveLang;
            node["canon_lang_source"] = wire.LangSource;
            node["canon_stack"] = JsonSerializer.SerializeToNode(wire.CanonStack);
            return node.ToJsonString(Pretty);
        }
        catch
        {
            return openJson;
        }
    }

    internal static CanonStackWire? TryBuildStack(
        SessionContext session,
        CdpSettings cdpSettings,
        DocumentBufferStore docStore)
    {
        var scm = session.ScmRoot ?? session.ProjectRoot;
        if (string.IsNullOrWhiteSpace(scm))
            return null;

        var host = WritingCanonHostPathsResolver.FromSession(session, cdpSettings, docStore);
        var stack = WritingCanonStackResolver.Build(scm, host);
        return new CanonStackWire(
            stack.ScmRoot,
            stack.SettingsPath,
            stack.SettingsSource,
            stack.EffectiveLang,
            stack.LangSource,
            new Dictionary<string, object>
            {
                ["operator"] = stack.Operator.Select(ToWire).ToList(),
                ["code"] = stack.Code.Select(ToWire).ToList(),
            });
    }

    private static object BuildHostPaths(
        SessionContext session,
        CdpSettings cdpSettings,
        DocumentBufferStore docStore)
    {
        var host = WritingCanonHostPathsResolver.FromSession(session, cdpSettings, docStore);
        return new
        {
            primary_knowledge_root = host.PrimaryKnowledgeRoot,
            guiders_style_root = host.GuidersStyleRoot,
            session_language = host.SessionLanguage,
            buffer_language = host.BufferLanguage,
            notes_config = cdpSettings.Memory.NotesConfig,
        };
    }

    private static object ToWire(WritingCanonStackEntry e) => new
    {
        layer = e.Layer,
        plane = e.Plane.ToString().ToLowerInvariant(),
        path = e.Path,
        exists = e.Exists,
        budget = e.Budget,
        preview = e.Preview,
        source = e.Source,
    };

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
}
