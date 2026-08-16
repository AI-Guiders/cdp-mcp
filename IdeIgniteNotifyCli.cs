#nullable enable

namespace CdpMcp;

/// <summary>Headless <c>--ignite-notify</c> for supervisor lifecycle fallback (Composer CDT).</summary>
internal static class IdeIgniteNotifyCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        var idx = Array.IndexOf(args, "--ignite-notify");
        if (idx < 0 || idx + 1 >= args.Length)
            return 2;

        var eventName = args[idx + 1];
        var ok = !args.Contains("--fail", StringComparer.OrdinalIgnoreCase);
        var pulse = ReadValue(args, "--pulse");
        var detail = ReadValue(args, "--detail");
        var seat = ReadValue(args, "--seat");
        var armId = ReadValue(args, "--arm-id");

        if (!string.IsNullOrWhiteSpace(seat))
            Environment.SetEnvironmentVariable("CDP_IGNITE_SEAT", seat);

        IdeIgniteArmHost.EnsureStarted();
        IdeIgniteArmHost.Notify(eventName, ok, pulse, detail);

        if (!string.IsNullOrWhiteSpace(armId))
            await IdeIgniteArmHost.WaitForArmDeliveryAsync(armId, TimeSpan.FromSeconds(120)).ConfigureAwait(false);
        else
            await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);

        return 0;
    }

    static string? ReadValue(string[] args, string flag)
    {
        var i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
