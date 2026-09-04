#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeIgniteChannel
{
    static async Task<object> SendAsync(
        int port,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken ct)
    {
        var message = Opt(args, "message") ?? Opt(args, "text") ?? Opt(args, "msg");
        if (string.IsNullOrWhiteSpace(message))
            return Err("send", "message_required", "send message=…", port);

        var chat = Opt(args, "chat") ?? Opt(args, "title") ?? Opt(args, "agent");
        if (string.IsNullOrWhiteSpace(chat))
            chat = IdeIgniteArmHost.ResolveChatFromTenantLatch(
                CdpTenantExecutionContext.CurrentSlice?.Key.Wire,
                CdpTenantRoutingContext.CurrentConversationId,
                null);
        var waitSec = OptInt(args, "wait_seconds") ?? OptInt(args, "timeout") ?? 90;

        // Harness dispatch parity with arm fire: opencode session (ses_…/harness=opencode) → native channel;
        // everything else → Cursor CDT (legacy default seat).
        var harness = Opt(args, "harness")?.Trim().ToLowerInvariant();
        var ocSession = Opt(args, "session") ?? Opt(args, "opencode_session");
        var isOpencode =
            string.Equals(harness, "opencode", StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(harness)
                && ocSession is { Length: > 0 } s
                && s.StartsWith("ses_", StringComparison.Ordinal));

        if (isOpencode)
        {
            var oc = await FireToOpencodeAsync(message!, ct, ocSession).ConfigureAwait(false);
            return new
            {
                schema = Schema,
                ok = oc.GetType().GetProperty("ok")?.GetValue(oc) is true,
                op = "send",
                channel = "opencode",
                detail = oc,
                port
            };
        }

        return await FireAsync(port, message!, chat, waitSec, ct).ConfigureAwait(false);
    }

    static object Err(string op, string error, string detail, int port) => new
    {
        schema = Schema,
        ok = false,
        op,
        error,
        detail,
        port,
        go = GoName,
        tool = ToolName,
        hint = "probe first; Cursor must listen on CDT port."
    };
}
