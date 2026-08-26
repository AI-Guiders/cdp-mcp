using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

internal static class IdeCanonChannel
{
    internal static string HandleJson(
        SessionContext session,
        CdpSettings cdpSettings,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var scm = session.ScmRoot ?? session.ProjectRoot;
        if (string.IsNullOrWhiteSpace(scm))
            return JsonSerializer.Serialize(new
            {
                ok = false,
                error = "need_scm_root",
                hint = "cdp_open first — canon stack needs session scm_root.",
            });

        var host = WritingCanonHostPathsResolver.FromCdpSettings(cdpSettings);
        var stack = WritingCanonStackResolver.Build(scm, host);
        return JsonSerializer.Serialize(new
        {
            ok = true,
            scm_root = stack.ScmRoot,
            settings_path = stack.SettingsPath,
            settings_source = stack.SettingsSource,
            host_paths = new
            {
                primary_knowledge_root = host.PrimaryKnowledgeRoot,
                guiders_style_root = host.GuidersStyleRoot,
                notes_config = cdpSettings.Memory.NotesConfig,
            },
            canon_stack = new Dictionary<string, object>
            {
                ["operator"] = stack.Operator.Select(ToWire).ToList(),
                ["code"] = stack.Code.Select(ToWire).ToList(),
            },
        });
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
}
