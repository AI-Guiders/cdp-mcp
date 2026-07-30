#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeIgniteArmHost
{
    public const string LeafWakeArmId = "leaf-wake";

    /// <summary>
    /// Continuity arm for the current TM leaf. Stable id so the next leaf supersedes the prior.
    /// Short timer so the shot lands after the agent ends the take/done turn (not mid-Stop).
    /// </summary>
    public static object ArmForLeaf(string taskTitle, string reason)
    {
        if (string.IsNullOrWhiteSpace(taskTitle))
            return Err("arm", "leaf_title_required", "ArmForLeaf needs task title");

        var args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["when"] = JsonSerializer.SerializeToElement("timer"),
            ["in"] = JsonSerializer.SerializeToElement("2s"),
            ["task"] = JsonSerializer.SerializeToElement(taskTitle.Trim()),
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
            task = taskTitle.Trim(),
            arm = result,
            hint = "Leaf continuity armed (2s). End turn — AutoI fires wake for this leaf."
        };
    }
}
