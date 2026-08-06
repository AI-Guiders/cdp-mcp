#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeIgniteArmHost
{
    public const string LeafWakeArmId = "leaf-wake";

    /// <summary>
    /// Continuity arm for the current TM leaf. Stable id so the next leaf supersedes the prior.
    /// Short timer so the shot lands after the agent ends the take/done turn (not mid-Stop).
    /// Invent-only Hold uses 15m — DIG REJECT mill ≠ 2s/3m Recover thrash (same family as last_once softener).
    /// </summary>
    public static object ArmForLeaf(string taskTitle, string reason)
    {
        if (!IsAutonomousArmed())
            return Err("leaf_arm", "autonomous_off", "ArmForLeaf skipped — autonomous continuity is off (op=autonomous_off / halt)");

        if (string.IsNullOrWhiteSpace(taskTitle))
            return Err("arm", "leaf_title_required", "ArmForLeaf needs task title");

        var title = taskTitle.Trim();
        var inventOnlyHold = IsInventOnlyHoldTask(title);
        var inRaw = inventOnlyHold ? "15m" : "2s";

        var args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["when"] = JsonSerializer.SerializeToElement("timer"),
            ["in"] = JsonSerializer.SerializeToElement(inRaw),
            ["task"] = JsonSerializer.SerializeToElement(title),
            ["id"] = JsonSerializer.SerializeToElement(LeafWakeArmId),
            ["once"] = JsonSerializer.SerializeToElement(true),
            ["charge"] = JsonSerializer.SerializeToElement("minimal"),
            ["settle_seconds"] = JsonSerializer.SerializeToElement(1),
            ["force"] = JsonSerializer.SerializeToElement(true)
        };

        var result = Arm(args);
        return new
        {
            schema = IdeIgniteChannel.Schema,
            ok = true,
            op = "leaf_arm",
            reason,
            task = title,
            invent_only_hold = inventOnlyHold,
            in_raw = inRaw,
            arm = result,
            hint = ArmForLeafHint(IsAutonomousArmed(), inventOnlyHold)
        };
    }

    /// <summary>ArmForLeaf tip — under autonomous do not teach End-turn park; invent-only Hold ≠ 2s DIG REJECT mill.</summary>
    internal static string ArmForLeafHint(bool autonomous, bool inventOnlyHold = false)
    {
        if (inventOnlyHold)
        {
            return autonomous
                ? "Leaf continuity armed (15m invent-only Hold). DIG REJECT mill ≠ 2s/3m Recover thrash — AutoI is insurance if the thread dies, not a license to park."
                : "Leaf continuity armed (15m invent-only Hold). End turn — AutoI fires wake for this leaf.";
        }

        return autonomous
            ? "Leaf continuity armed (2s). Keep flying the leaf — AutoI is insurance if the thread dies, not a license to park."
            : "Leaf continuity armed (2s). End turn — AutoI fires wake for this leaf.";
    }
}
