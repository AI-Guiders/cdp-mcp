#nullable enable

namespace CdpMcp;

/// <summary>
/// OpenCode fire channel for AutoIgnition — THIN dispatcher over the single Opencode
/// wake channel (CideWakeChannels, ADR-0213). Harness is one, the channel is one;
/// only the payload (wake text) differs. No transport lives here.
/// </summary>
internal static partial class IdeIgniteChannel
{
    public static bool IsOpencodeConfigured() => CideWakeChannels.Opencode.IsConfigured();

    public static async Task<object> FireToOpencodeAsync(
        string message,
        CancellationToken ct,
        string? session = null)
    {
        // Per-arm wake target wins — the arm is the SSOT for the target session.
        if (string.IsNullOrWhiteSpace(session))
            return CideWakeChannels.Opencode.ErrNoSession();

        // CLI first — `opencode run -s <session>` inherits the local identity (auth-free).
        // HTTP (desktop sidecar / explicit server) — fallback when the CLI path fails.
        var cli = await CideWakeChannels.Opencode
            .SendCliAsync(session, message, ct).ConfigureAwait(false);
        if (CideWakeChannels.IsOk(cli))
            return cli;

        var url = await CideWakeChannels.Opencode
            .TryEnsureServerUrlAsync(ct).ConfigureAwait(false);
        if (url is null)
            return cli; // честный CLI-файл — ретрай решает диспетчер/тик

        return await CideWakeChannels.Opencode
            .SendHttpAsync(url, session, message, ct).ConfigureAwait(false);
    }
}