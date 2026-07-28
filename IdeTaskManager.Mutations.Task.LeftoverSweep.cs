#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdeTaskManager
{
    /// <summary>
    /// Dry-run (default) or apply: close parked/deferred leftovers whose AC+DoD are all met.
    /// Never steals focus. DoR is ignored for ship readiness.
    /// REPL: <c>leftover</c> | <c>leftover apply</c>.
    /// </summary>
    static object TaskLeftoverSweep(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var apply = IsTruthy(args, "apply")
                    || IsTruthy(args, "commit")
                    || string.Equals(Title(args), "apply", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        Opt(args, "action") ?? OptGoArg(args, "action"),
                        "apply",
                        StringComparison.OrdinalIgnoreCase);

        var includeFocus = IsTruthy(args, "include_focus")
                           || IsTruthy(args, "include_active");

        var candidates = store.StageListLeftoverShipReady(state, includeFocus);
        var focusBefore = state.ActiveStageId;

        if (!apply)
        {
            return new
            {
                op = "leftover",
                dry_run = true,
                apply = false,
                count = candidates.Count,
                candidates = candidates.Select(c => new
                {
                    task_id = c.TaskId,
                    title = c.Title,
                    status = c.Status,
                    criteria = c.CriteriaSummary
                }).ToList(),
                hint = candidates.Count == 0
                    ? "no parked/deferred leftovers with all AC+DoD met — leftover apply is a no-op"
                    : "leftover apply — mark candidates done without stealing focus"
            };
        }

        var closed = new List<object>();
        foreach (var c in candidates)
        {
            // Never call FocusStage — TaskDone-by-id pattern for non-active.
            var r = store.StageSetStatus(state, c.TaskId, "done");
            closed.Add(new { task_id = r.stage_id, title = c.Title, from_status = c.Status, status = r.status });
        }

        store.WorkFocusSave(state);

        return new
        {
            op = "leftover",
            dry_run = false,
            apply = true,
            closed_count = closed.Count,
            closed,
            focus_preserved = state.ActiveStageId == focusBefore,
            active_stage_id = state.ActiveStageId,
            hint = closed.Count == 0
                ? "nothing to close — AC+DoD not fully met on parked/deferred"
                : $"closed {closed.Count} leftover(s); focus unchanged"
        };
    }

    static bool IsTruthy(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (TryTruthyElement(args, key, out var ok))
            return ok;
        if (args.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object
            && ga.TryGetProperty(key, out var nested)
            && TryTruthyJson(nested, out ok))
            return ok;
        return false;
    }

    static bool TryTruthyElement(
        IReadOnlyDictionary<string, JsonElement> args,
        string key,
        out bool value)
    {
        value = false;
        if (!args.TryGetValue(key, out var el))
            return false;
        return TryTruthyJson(el, out value);
    }

    static bool TryTruthyJson(JsonElement el, out bool value)
    {
        value = false;
        switch (el.ValueKind)
        {
            case JsonValueKind.True:
                value = true;
                return true;
            case JsonValueKind.False:
                value = false;
                return true;
            case JsonValueKind.String:
            {
                var s = (el.GetString() ?? "").Trim().ToLowerInvariant();
                if (s is "1" or "true" or "yes" or "on" or "apply")
                {
                    value = true;
                    return true;
                }

                if (s is "0" or "false" or "no" or "off")
                {
                    value = false;
                    return true;
                }

                return false;
            }
            case JsonValueKind.Number:
                if (el.TryGetInt32(out var n))
                {
                    value = n != 0;
                    return true;
                }

                return false;
            default:
                return false;
        }
    }
}
