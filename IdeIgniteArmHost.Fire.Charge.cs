#nullable enable
using System.Globalization;

namespace CdpMcp;

/// <summary>Charge-mode predicates + template expand (≤ADX soft-warn peel).</summary>
internal static partial class IdeIgniteArmHost
{
    static bool IsCustomChargeMode(string? mode)
    {
        var m = (mode ?? "minimal").Trim().ToLowerInvariant();
        return m is "custom" or "expand" or "legacy";
    }

    static bool IsRemountChargeMode(string? mode) =>
        string.Equals(
            (mode ?? "").Trim(),
            IdeRemountWake.ChargeMode,
            StringComparison.OrdinalIgnoreCase);

    static bool IsOomChargeMode(string? mode) =>
        string.Equals(
            (mode ?? "").Trim(),
            IdeOomWake.ChargeMode,
            StringComparison.OrdinalIgnoreCase);

    static bool IsEscalateChargeMode(string? mode) =>
        string.Equals(
            (mode ?? "").Trim(),
            HildEscalateChargeMode,
            StringComparison.OrdinalIgnoreCase);

    static string Expand(string template, IgniteArm arm, bool ok, string? pulse, string? detail)
    {
        var t = template
            .Replace("{event}", IdeIgniteChannel.EventTokenForCharge(arm.Event), StringComparison.OrdinalIgnoreCase)
            .Replace("{task}", arm.Task ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{ok}", ok ? "ok" : "fail", StringComparison.OrdinalIgnoreCase)
            .Replace("{pulse}", pulse ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{detail}", detail ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{id}", arm.Id, StringComparison.OrdinalIgnoreCase)
            .Replace("{when}", DateTimeOffset.UtcNow.ToString("u", CultureInfo.InvariantCulture),
                StringComparison.OrdinalIgnoreCase);
        return t;
    }
}
