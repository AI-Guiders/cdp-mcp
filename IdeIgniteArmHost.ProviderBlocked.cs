#nullable enable
using System.Text;

namespace CdpMcp;

internal static partial class IdeIgniteArmHost
{
    /// <summary>Arm status after post-fire provider refusal (not awaiting_operator success latch).</summary>
    internal const string ProviderBlockedStatus = "provider_blocked";

    internal const string NewThreadRequiredError = "new_thread_required";

    static Action<IgniteArm>? ProviderBlockedHook;

    internal static void RegisterProviderBlockedHook(Action<IgniteArm>? hook) => ProviderBlockedHook = hook;

    internal static bool IsProviderBlockedStatus(string? status) =>
        string.Equals(status, ProviderBlockedStatus, StringComparison.Ordinal);

    internal static bool ShouldEnterProviderBlockedContinuity(string? fireError) =>
        IdeIgniteChannel.IsProviderBlockedError(fireError);

    static void EnterProviderBlockedContinuity(IgniteArm arm, string? detail)
    {
        SetStatus(arm.Id, ProviderBlockedStatus, IdeIgniteChannel.ProviderBlockedError, fired: DateTimeOffset.UtcNow);
        TryStashNewPfHandoff(arm, detail);
        try
        {
            ProviderBlockedHook?.Invoke(Clone(arm));
        }
        catch
        {
            /* best-effort */
        }
    }

    static void TryStashNewPfHandoff(IgniteArm arm, string? detail)
    {
        var body = BuildProviderBlockedHandoffBody(arm, detail);
        var plan = string.IsNullOrWhiteSpace(arm.Task) ? "Task Manager SSOT — cdp_cockpit go=plan" : arm.Task;
        var ignite =
            "AutoIgnition provider_blocked after fire. Open NEW chat for PF; re-ARM only on new title. Blocked thread = provenance only.";
        IdePressureChannel.StashAutoIgnitionHandoff(body, ignite, plan);
    }

    static string BuildProviderBlockedHandoffBody(IgniteArm arm, string? detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# AutoIgnition provider_blocked");
        sb.AppendLine();
        sb.AppendLine("## Continuity");
        sb.AppendLine("- state: provider_blocked / new_thread_required");
        sb.AppendLine("- fail closed: do not retry fire in blocked chat");
        sb.AppendLine("- Task Manager: unchanged (WitDB SSOT) — new PF reads `go=plan`");
        sb.AppendLine("- habitat: CDP");
        sb.AppendLine();
        sb.AppendLine("## Arm");
        sb.AppendLine($"- id: {arm.Id}");
        sb.AppendLine($"- chat: {arm.Chat ?? "(agents default)"}");
        sb.AppendLine($"- task: {arm.Task ?? "—"}");
        sb.AppendLine($"- event: {arm.Event}");
        if (!string.IsNullOrWhiteSpace(detail))
            sb.AppendLine($"- detail: {detail.Trim()}");
        sb.AppendLine();
        sb.AppendLine("## New PF bootstrap");
        sb.AppendLine("1. cdp_pressure op=recall");
        sb.AppendLine("2. cdp_cockpit go=plan (Task Manager board)");
        sb.AppendLine("3. Continue active stage; disarm old ignite / resume only after operator opens new chat");
        return sb.ToString();
    }
}
