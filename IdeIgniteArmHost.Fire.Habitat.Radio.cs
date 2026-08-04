#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Autoi → Glass Intercom = Radio face (I6), not Composer charge wall.
/// Dual-seat claim: live+debug both fire → one Intercom/FDR spine.
/// </summary>
internal static partial class IdeIgniteArmHost
{
    const int HabitatIntercomRadioKeepChars = 240;

    static bool? PrimaryAutoiSeatOverride;

    /// <summary>Tests: force primary/non-primary without install root. null = derive from <see cref="Seat"/>.</summary>
    internal static void BindPrimaryAutoiSeat(bool? primary) => PrimaryAutoiSeatOverride = primary;

    /// <summary>
    /// Live <c>cdp</c> owns Glass Intercom Autoi voice + prefer_autonomous FDR.
    /// <c>cdp-debug</c> twin doubles wake_habitat* tape (lived ~174/h).
    /// </summary>
    internal static bool IsPrimaryAutoiSeat()
    {
        if (PrimaryAutoiSeatOverride is { } o)
            return o;
        return !string.Equals(Seat, "cdp-debug", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Cross-seat: same arm_id mirrored once within window (dual Autoi claim).</summary>
    internal static bool TryClaimSharedWakeMirror(string armId)
    {
        if (string.IsNullOrWhiteSpace(armId))
            return true;

        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "cdp-mcp",
                "intercom-autoi-mirror-claim.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var now = DateTimeOffset.UtcNow;

            if (File.Exists(path))
            {
                var raw = File.ReadAllText(path);
                var prev = JsonSerializer.Deserialize<WakeMirrorClaim>(raw, JsonOpts);
                if (prev is not null
                    && string.Equals(prev.ArmId, armId, StringComparison.OrdinalIgnoreCase)
                    && now - prev.StampedUtc < TimeSpan.FromSeconds(20))
                    return false;
            }

            var claim = new WakeMirrorClaim
            {
                ArmId = armId.Trim(),
                Seat = Seat,
                StampedUtc = now
            };
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(claim, JsonOpts));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Normalize dual-seat system wakes onto one claim key (remount-* ids differ per seat).</summary>
    internal static string MirrorClaimKey(IgniteArm arm)
    {
        if (IsRemountWakeArm(arm))
            return "family:remount";
        if (IsHildEscalateWakeArm(arm))
            return "family:escalate";
        if (IsHildAwayWakeArm(arm))
            return "family:hild_away";
        if (IsOomWakeArm(arm))
            return "family:oom";
        if (IsToolWakeArmId(arm.Id))
            return "family:tool";
        if (string.Equals(arm.Id, LeafWakeArmId, StringComparison.OrdinalIgnoreCase))
            return "family:leaf-wake";
        if (string.Equals(arm.Id, AutonomousSeedArmId, StringComparison.OrdinalIgnoreCase))
            return "family:autonomous-seed";
        return arm.Id;
    }

    /// <summary>
    /// Composer charge → Radio pointer for Glass. Short test charges pass through unchanged.
    /// </summary>
    internal static string FormatHabitatIntercomRadio(IgniteArm? arm, string charge)
    {
        var t = (charge ?? "").Trim();
        if (t.Length == 0)
            return "Autoi · wake\n→ PFD.NEXT";

        if (t.Length <= HabitatIntercomRadioKeepChars && !LooksLikeComposerChargeWall(t))
            return t;

        var tag = ClassifyWakeRadioTag(arm);
        var leaf = !string.IsNullOrWhiteSpace(arm?.Task)
            ? OneLineRadio(arm!.Task!, 48)
            : OneLineRadio(FirstNonEmptyLine(t), 48);
        if (string.IsNullOrWhiteSpace(leaf))
            leaf = "TM leaf";

        return $"Autoi · {tag}\n→ PFD.NEXT\ndelta → Plan · {leaf}";
    }

    static bool LooksLikeComposerChargeWall(string t) =>
        t.Contains("operator_priority", StringComparison.OrdinalIgnoreCase)
        || t.Contains("Habitat=CDP", StringComparison.Ordinal)
        || t.Contains("thread amnesia", StringComparison.OrdinalIgnoreCase)
        || t.Contains("Human-face axe", StringComparison.OrdinalIgnoreCase)
        || t.Contains('\n') && t.Length > 120;

    static string ClassifyWakeRadioTag(IgniteArm? arm)
    {
        if (arm is null)
            return "wake";
        if (IsRemountWakeArm(arm))
            return "remount";
        if (IsHildEscalateWakeArm(arm))
            return "escalate";
        if (IsHildAwayWakeArm(arm))
            return "away";
        if (IsOomWakeArm(arm))
            return "oom";
        if (IsToolWakeArmId(arm.Id))
            return "tool";
        if (string.Equals(arm.Id, LeafWakeArmId, StringComparison.OrdinalIgnoreCase))
            return "leaf";
        if (string.Equals(arm.Id, AutonomousSeedArmId, StringComparison.OrdinalIgnoreCase))
            return "seed";
        return "wake";
    }

    static string FirstNonEmptyLine(string t)
    {
        foreach (var raw in t.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length > 0)
                return line;
        }

        return "";
    }

    static string OneLineRadio(string s, int max)
    {
        var t = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (t.Contains("  ", StringComparison.Ordinal))
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        if (t.Length > max)
            t = t[..(max - 1)].TrimEnd() + "…";
        return t;
    }

    sealed class WakeMirrorClaim
    {
        public string ArmId { get; set; } = "";
        public string Seat { get; set; } = "";
        public DateTimeOffset StampedUtc { get; set; }
    }
}
