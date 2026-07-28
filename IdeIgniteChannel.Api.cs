#nullable enable
using System.Net.Http;
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeIgniteChannel
{
    public static string HandleJson(IReadOnlyDictionary<string, JsonElement>? args) =>
        JsonSerializer.Serialize(Handle(args), Pretty);

    public static async Task<string> HandleJsonAsync(
        IReadOnlyDictionary<string, JsonElement>? args,
        CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await HandleAsync(args, cancellationToken).ConfigureAwait(false), Pretty);

    public static object Handle(IReadOnlyDictionary<string, JsonElement>? args = null) =>
        HandleAsync(args, CancellationToken.None).GetAwaiter().GetResult();

    public static async Task<object> HandleAsync(
        IReadOnlyDictionary<string, JsonElement>? args = null,
        CancellationToken cancellationToken = default)
    {
        IdeIgniteArmHost.EnsureStarted();
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        var port = OptInt(args, "port") ?? DefaultPort;

        try
        {
            return op switch
            {
                "probe" or "caps" or "status" or "scene" =>
                    await ProbeAsync(port, cancellationToken).ConfigureAwait(false),
                "chats" or "list_chats" =>
                    await ChatsAsync(port, cancellationToken).ConfigureAwait(false),
                "send" or "ignite" or "fire" =>
                    await SendAsync(port, args, cancellationToken).ConfigureAwait(false),
                "arm" or "schedule" or "wake" =>
                    IdeIgniteArmHost.Arm(args),
                "disarm" or "cancel" or "unarm" =>
                    IdeIgniteArmHost.Disarm(args),
                "list" or "arms" or "alarms" =>
                    IdeIgniteArmHost.List(),
                "hygiene" or "scrub" or "clean" =>
                    IdeIgniteArmHost.Hygiene(),
                "plateau" =>
                    IdeIgniteArmHost.Plateau(),
                "continuity" or "pulse" =>
                    IdeIgniteArmHost.Continuity(),
                "resume" or "clear_await" or "unawait" =>
                    IdeIgniteArmHost.Resume(args),
                "delivery" or "delivery_evidence" =>
                    IdeIgniteArmHost.Delivery(args),
                "watchdog" or "transcript_watchdog" or "observe" =>
                    IdeIgniteArmHost.Watchdog(args),
                _ => await ProbeAsync(port, cancellationToken).ConfigureAwait(false)
            };
        }
        catch (Exception ex)
        {
            return Err(op, "exception", ex.Message, port);
        }
    }
}
