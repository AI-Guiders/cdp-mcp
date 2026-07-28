#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeIgniteChannel
{
    static async Task<object> ProbeAsync(int port, CancellationToken ct)
    {
        object? version = null;
        object? pages = null;
        string? error = null;
        try
        {
            version = await GetJsonAsync(port, "/json/version", ct).ConfigureAwait(false);
            pages = await GetJsonAsync(port, "/json/list", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        ComposerState? state = null;
        string? pageTitle = null;
        if (error is null)
        {
            await using var session = await CdtSession.ConnectPageAsync(port, ct).ConfigureAwait(false);
            pageTitle = session.PageTitle;
            state = await session.EvalStateAsync(ct).ConfigureAwait(false);
        }

        var kind = AriaKind(state?.SubmitAria);
        var blocked = state?.ProviderBlocked == true;
        var arms = IdeIgniteArmHost.SceneSlice();
        var continuity = IdeIgniteArmHost.ContinuitySlice();
        return new
        {
            schema = Schema,
            ok = error is null && state is { HasInput: true } && !blocked,
            op = "scene",
            go = GoName,
            tool = ToolName,
            pulse = error is null
                ? blocked
                    ? $"ignite · {ProviderBlockedError} · {pageTitle ?? "?"} · fail closed"
                    : $"ignite · cdt :{port} · {pageTitle ?? "?"} · {kind} · {IdeIgniteArmHost.ContinuityPulseLine()}"
                : $"ignite · cdt :{port} · down · {IdeIgniteArmHost.ContinuityPulseLine()}",
            port,
            page_title = pageTitle,
            submit_kind = kind,
            state,
            arms,
            continuity,
            version,
            pages,
            error,
            hint = error is null
                ? "op=send|arm|hygiene|plateau|continuity. Idle=Voice; never click Voice/Stop."
                : "Start Cursor via tools/Start-Cursor-WithCdt.ps1 (remote-debugging-port + allow-origins)."
        };
    }

    static async Task<object> ChatsAsync(int port, CancellationToken ct)
    {
        await using var session = await CdtSession.ConnectPageAsync(port, ct).ConfigureAwait(false);
        var chats = await session.EvalAsync<JsonElement>(ChatListJs, ct).ConfigureAwait(false);
        return new
        {
            schema = Schema,
            ok = true,
            op = "chats",
            go = GoName,
            tool = ToolName,
            pulse = $"ignite · chats · {session.PageTitle}",
            port,
            page_title = session.PageTitle,
            chats,
            hint = "op=send chat=\"CCR script report desk\" message=…"
        };
    }
}
