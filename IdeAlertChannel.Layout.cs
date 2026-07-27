#nullable enable
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdeAlertChannel
{
    /// <summary>
    /// Phase-aware desk layout hint — suggest, never auto-mutate sticky seats.
    /// </summary>
    public static (string? LayoutHint, string? SeatNote) SuggestLayout(
        CdpPhase phase,
        CdpObjectKind obj,
        IReadOnlyDictionary<string, string?> seats)
    {
        var p = Seat(seats, "p");
        var m = Seat(seats, "m");
        var codeish = obj is CdpObjectKind.Code or CdpObjectKind.Repo or CdpObjectKind.Issue;

        // Sticky plugins after dogfood while doing code work — common SA trap.
        if (IsPlugins(p) && codeish)
        {
            return phase is CdpPhase.Explore
                ? ("code+net", "P=plugins stale — layout=code+net")
                : ("agent", "P=plugins stale — layout=agent");
        }

        if ((phase is CdpPhase.Verify or CdpPhase.Act or CdpPhase.Review) && codeish)
        {
            if (m is not null && IsBrowser(m))
                return ("code+shell", "M=browser — layout=code+shell for act/verify");
            if (m is not null && IsPlan(m))
                return ("code+shell", $"M={m} — layout=code+shell for act/verify");
        }

        if (phase is CdpPhase.Explore && obj is CdpObjectKind.Code
            && m is not null && IsPlan(m))
            return ("code+net", "M=plan — layout=code+net for explore");

        if (phase is CdpPhase.Plan && p is not null && !IsPlan(p))
            return ("agent", $"P={p} — layout=agent (plan|editor|script)");

        return (null, null);
    }

    static string? Seat(IReadOnlyDictionary<string, string?> seats, string id) =>
        seats.TryGetValue(id, out var v) && v is { Length: > 0 } ? v : null;

    static bool IsPlugins(string? pin) =>
        pin is "plugins" or "plugin" or "vsix";

    static bool IsPlan(string? pin) =>
        pin is "plan" or "work" or "tasks" or "tm" or "feature" or "task";

    static bool IsBrowser(string? pin) =>
        pin is "browser" or "scene_internet_browser" or "internet_browser";

    static bool IsShellOrGitOrTest(string? pin) =>
        pin is "shell_scene" or "shell" or "git_scene" or "git" or "test_scene" or "test" or "ecl" or "chk";
}
