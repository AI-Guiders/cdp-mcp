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
        var waitSec = OptInt(args, "wait_seconds") ?? OptInt(args, "timeout") ?? 90;
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
