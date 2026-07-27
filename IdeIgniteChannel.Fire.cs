#nullable enable

namespace CdpMcp;

internal static partial class IdeIgniteChannel
{
    /// <summary>CDT inject used by send= and by ARM host after event/timer.</summary>
    public static async Task<object> FireAsync(
        int port,
        string message,
        string? chat,
        int waitSec,
        CancellationToken ct)
    {
        waitSec = Math.Clamp(waitSec, 5, 600);
        await using var session = await CdtSession.ConnectPageAsync(port, ct).ConfigureAwait(false);

        var focusErr = await TryFocusChatAsync(session, chat, port, ct).ConfigureAwait(false);
        if (focusErr is not null)
            return focusErr;

        var idle = await WaitUntilIdleAsync(session, waitSec, ct).ConfigureAwait(false);
        if (idle is null)
            return Err("send", "busy_timeout", $"submit stayed Stop/Queue for {waitSec}s", port);

        var inserted = await session.EvalAsync<InsertResult>(InsertJs(message), ct).ConfigureAwait(false);
        if (inserted is not { Ok: true } || inserted.Len < 1)
            return new { schema = Schema, ok = false, op = "send", error = "insert_failed", inserted, port };

        var sendGate = await WaitUntilSendAsync(session, port, ct).ConfigureAwait(false);
        if (sendGate is not null)
            return sendGate;

        var click = await session.EvalAsync<ClickResult>(ClickSendJs, ct).ConfigureAwait(false);
        if (click is not { Ok: true })
            return new { schema = Schema, ok = false, op = "send", error = "click_failed", click, port };

        await Task.Delay(500, ct).ConfigureAwait(false);
        var after = await session.EvalStateAsync(ct).ConfigureAwait(false);

        return new
        {
            schema = Schema,
            ok = true,
            op = "send",
            go = GoName,
            tool = ToolName,
            pulse = $"ignite · sent · {AriaKind(after.SubmitAria)}",
            port,
            chat,
            page_title = session.PageTitle,
            inserted,
            click,
            after,
            submit_kind_after = AriaKind(after.SubmitAria),
            hint = "Expect new user turn in target chat when host accepts Send."
        };
    }

    static async Task<object?> TryFocusChatAsync(
        CdtSession session,
        string? chat,
        int port,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(chat))
            return null;

        var focus = await session.EvalAsync<FocusChatResult>(FocusChatJs(chat), ct).ConfigureAwait(false);
        if (focus is { Ok: true })
        {
            await Task.Delay(400, ct).ConfigureAwait(false);
            return null;
        }

        return new
        {
            schema = Schema,
            ok = false,
            op = "send",
            error = "chat_not_found",
            chat,
            focus,
            port,
            hint = "op=chats to list titles; substring match on Chat title button."
        };
    }

    static async Task<ComposerState?> WaitUntilIdleAsync(
        CdtSession session,
        int waitSec,
        CancellationToken ct)
    {
        for (var i = 0; i < waitSec; i++)
        {
            ct.ThrowIfCancellationRequested();
            var st = await session.EvalStateAsync(ct).ConfigureAwait(false);
            var kind = AriaKind(st.SubmitAria);
            if (st.HasInput && kind is not ("stop" or "queue"))
                return st;

            await Task.Delay(1000, ct).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>null = ready to click Send; otherwise error payload.</summary>
    static async Task<object?> WaitUntilSendAsync(
        CdtSession session,
        int port,
        CancellationToken ct)
    {
        for (var i = 0; i < 40; i++)
        {
            ct.ThrowIfCancellationRequested();
            var st = await session.EvalStateAsync(ct).ConfigureAwait(false);
            var kind = AriaKind(st.SubmitAria);
            if (kind == "send")
                return null;

            if (kind == "stop")
                return Err("send", "became_stop", "generation started before click", port);

            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        return Err("send", "not_send", "button never became Send (TipTap text not accepted? still Voice?)", port);
    }
}
