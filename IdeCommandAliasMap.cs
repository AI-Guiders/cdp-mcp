#nullable enable
using System.Collections.Frozen;
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Bucket A aliases: CIDE Intent Melody <c>command_id</c> → CDP ICM tool (+ optional default args).
/// For future on-demand GUI / human HCI client. Does not rewrite MCP CallTool names automatically.
/// See <c>docs/design/icm-melody-cdp-inventory.md</c>. Melody catalog itself is not mutated here.
/// </summary>
public static class IdeCommandAliasMap
{
    public readonly record struct Resolved(
        string Tool,
        IReadOnlyDictionary<string, JsonElement>? Defaults,
        bool Identity);

    static readonly FrozenDictionary<string, Resolved> Map = Build();

    public static bool TryResolve(string? commandId, out Resolved resolved)
    {
        resolved = default;
        if (string.IsNullOrWhiteSpace(commandId))
            return false;
        return Map.TryGetValue(commandId.Trim(), out resolved);
    }

    /// <summary>Merge alias defaults under caller args (caller wins on key clash).</summary>
    public static IReadOnlyDictionary<string, JsonElement> MergeArgs(
        Resolved resolved,
        IReadOnlyDictionary<string, JsonElement>? callerArgs)
    {
        if (resolved.Defaults is null || resolved.Defaults.Count == 0)
            return callerArgs ?? FrozenDictionary<string, JsonElement>.Empty;
        if (callerArgs is null || callerArgs.Count == 0)
            return resolved.Defaults;

        var merged = new Dictionary<string, JsonElement>(resolved.Defaults, StringComparer.Ordinal);
        foreach (var (k, v) in callerArgs)
            merged[k] = v;
        return merged;
    }

    static FrozenDictionary<string, Resolved> Build()
    {
        var d = new Dictionary<string, Resolved>(StringComparer.Ordinal);

        void Id(string id) => d[id] = new Resolved(id, null, Identity: true);

        void To(string id, string tool, params (string Key, string Value)[] defaults)
        {
            IReadOnlyDictionary<string, JsonElement>? def = null;
            if (defaults.Length > 0)
            {
                var m = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var (k, v) in defaults)
                    m[k] = JsonSerializer.SerializeToElement(v);
                def = m;
            }

            d[id] = new Resolved(tool, def, Identity: string.Equals(id, tool, StringComparison.Ordinal));
        }

        // git_* — identity (domain already on ICM)
        foreach (var g in new[]
                 {
                     "git_branch", "git_commit", "git_diff", "git_fetch", "git_log",
                     "git_preflight", "git_preflight_fix_safe", "git_pull", "git_push",
                     "git_status", "git_submodule"
                 })
            Id(g);

        // build / test
        To("build", "cdp_build");
        To("build_structured", "cdp_build");
        To("build_solution_ui", "cdp_build");
        To("run_tests", "cdp_test");
        To("run_affected_tests", "cdp_test");

        // debug plane
        To("debug_launch", "cdp_debug", ("op", "launch"));
        To("debug_attach", "cdp_debug", ("op", "attach"));
        To("debug_continue", "cdp_debug", ("op", "continue"));
        To("debug_stop", "cdp_debug", ("op", "stop"));
        To("debug_step_into", "cdp_debug", ("op", "step_into"));
        To("debug_step_out", "cdp_debug", ("op", "step_out"));
        To("debug_step_over", "cdp_debug", ("op", "step_over"));

        // search / diags / open / edit (coarse)
        To("search_workspace_text", "cdp_search");
        To("get_current_file_diagnostics", "cdp_buffer", ("op", "diagnostics"));
        To("open_file", "cdp_buffer", ("op", "open"));
        To("load_solution", "cdp_open");
        To("apply_edit", "cdp_buffer", ("op", "edit"));

        // nav / land (GUI Anchor parity)
        To("focus_editor", "cdp_editor_scene");
        To("editor.reveal_code", "cdp_land");
        To("editor.select_code", "cdp_land");

        return d.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
