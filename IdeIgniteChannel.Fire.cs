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
        message = SanitizeComposerCharge(message);
        CdtSession session;
        try
        {
            session = await CdtSession.ConnectPageAsync(port, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("no_agent_composer", StringComparison.Ordinal)
            || ex.Message.StartsWith("no_page_target", StringComparison.Ordinal))
        {
            return Err("send", "no_agent_composer", ex.Message, port);
        }

        await using (session)
            return await FireOnSessionAsync(session, message, chat, port, waitSec, ct).ConfigureAwait(false);
    }

    static async Task<object> FireOnSessionAsync(
        CdtSession session,
        string message,
        string? chat,
        int port,
        int waitSec,
        CancellationToken ct)
    {
        var focusErr = await TryFocusChatAsync(session, chat, port, ct).ConfigureAwait(false);
        if (focusErr is not null)
            return focusErr;

        var idle = await WaitUntilIdleAsync(session, waitSec, ct).ConfigureAwait(false);
        if (idle is null)
            return Err("send", "busy_timeout", $"submit stayed Stop/Queue (or no ComposerScoped) for {waitSec}s", port);
        if (idle.ProviderBlocked)
            return ProviderBlockedResult("send", idle, port, "idle_wait");
        if (!idle.ComposerScoped)
            return Err("send", "wrong_surface",
                $"CDT page '{session.PageTitle}' has no agent composer (md/editor?). Open Cursor Agents.", port);

        var inserted = await session.EvalAsync<InsertResult>(InsertJs(message), ct).ConfigureAwait(false);
        if (string.Equals(inserted.Error, ProviderBlockedError, StringComparison.Ordinal))
            return ProviderBlockedResult("send", await session.EvalStateAsync(ct).ConfigureAwait(false), port, "insert", inserted.Blocked);
        if (inserted is not { Ok: true } || inserted.Len < 1)
            return new { schema = Schema, ok = false, op = "send", error = inserted.Error ?? "insert_failed", inserted, port, page_title = session.PageTitle };

        var sendGate = await WaitUntilSendAsync(session, port, ct).ConfigureAwait(false);
        if (sendGate is not null)
            return sendGate;

        var click = await session.EvalAsync<ClickResult>(ClickSendJs, ct).ConfigureAwait(false);
        if (click is not { Ok: true })
            return new { schema = Schema, ok = false, op = "send", error = "click_failed", click, port, page_title = session.PageTitle };

        var post = await WaitAfterSendAsync(session, ct).ConfigureAwait(false);
        if (post.Blocked)
            return ProviderBlockedResult("send", post.State!, port, "post_send", post.Probe);

        var after = post.State ?? await session.EvalStateAsync(ct).ConfigureAwait(false);
        if (after.ProviderBlocked)
            return ProviderBlockedResult("send", after, port, "post_send_recheck");

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

    static object ProviderBlockedResult(
        string op,
        ComposerState state,
        int port,
        string phase,
        ProviderBlockedProbe? probe = null) => new
    {
        schema = Schema,
        ok = false,
        op,
        error = ProviderBlockedError,
        detail = "Provider refusal visible in DOM — fail closed; do not treat error-card text as composer input.",
        phase,
        port,
        state,
        probe,
        go = GoName,
        tool = ToolName,
        pulse = $"ignite · {ProviderBlockedError} · {phase}",
        hint = "Await operator / new chat. op=resume clears latch after disarm. Re-read composer scoped to ui-prompt-input."
    };

    static async Task<(bool Blocked, ComposerState? State, ProviderBlockedProbe? Probe)> WaitAfterSendAsync(
        CdtSession session,
        CancellationToken ct)
    {
        for (var i = 0; i < 24; i++)
        {
            ct.ThrowIfCancellationRequested();
            var st = await session.EvalStateAsync(ct).ConfigureAwait(false);
            if (st.ProviderBlocked)
            {
                var probe = await session.EvalAsync<ProviderBlockedProbe>(ProviderBlockedJs, ct).ConfigureAwait(false);
                return (true, st, probe);
            }

            if (AriaKind(st.SubmitAria) is "stop")
                return (false, st, null);

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        var final = await session.EvalStateAsync(ct).ConfigureAwait(false);
        if (final.ProviderBlocked)
        {
            var probe = await session.EvalAsync<ProviderBlockedProbe>(ProviderBlockedJs, ct).ConfigureAwait(false);
            return (true, final, probe);
        }

        return (false, final, null);
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
            if (st.ProviderBlocked)
                return st;

            var kind = AriaKind(st.SubmitAria);
            // Idle = not Stop/Queue AND real agent composer (not md/editor editable).
            if (kind is not ("stop" or "queue") && st.ComposerScoped)
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
            if (st.ProviderBlocked)
                return ProviderBlockedResult("send", st, port, "pre_click");

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
