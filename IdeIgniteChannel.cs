#nullable enable
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=ignite_desk</c> / Meta <c>cdp_ignite</c> — AutoIgnition via Chrome DevTools (CDT)
/// into Cursor Composer (TipTap). Not Cognitive CDP; not UIA. Dogfood 2026-07-26.
/// Button states: Voice (empty) → Send (has text) → Stop (streaming) / Queue.
/// </summary>
internal static class IdeIgniteChannel
{
    public const string Schema = "ignite/v0";
    public const string ToolName = "cdp_ignite";
    public const string GoName = "ignite_desk";
    public const int DefaultPort = 9222;

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly JsonSerializerOptions Compact = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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
                _ => await ProbeAsync(port, cancellationToken).ConfigureAwait(false)
            };
        }
        catch (Exception ex)
        {
            return Err(op, "exception", ex.Message, port);
        }
    }

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
        return new
        {
            schema = Schema,
            ok = error is null && state is { HasInput: true },
            op = "scene",
            go = GoName,
            tool = ToolName,
            pulse = error is null
                ? $"ignite · cdt :{port} · {pageTitle ?? "?"} · {kind}"
                : $"ignite · cdt :{port} · down",
            port,
            page_title = pageTitle,
            submit_kind = kind,
            state,
            version,
            pages,
            error,
            hint = error is null
                ? "op=send message=… [chat=title][port=9222]. Idle=Voice; type→Send; never click Voice/Stop."
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

        await using var session = await CdtSession.ConnectPageAsync(port, ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(chat))
        {
            var focus = await session.EvalAsync<FocusChatResult>(FocusChatJs(chat), ct).ConfigureAwait(false);
            if (focus is not { Ok: true })
            {
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

            await Task.Delay(400, ct).ConfigureAwait(false);
        }

        ComposerState? idle = null;
        for (var i = 0; i < waitSec; i++)
        {
            ct.ThrowIfCancellationRequested();
            var st = await session.EvalStateAsync(ct).ConfigureAwait(false);
            var kind = AriaKind(st.SubmitAria);
            if (st.HasInput && kind is not ("stop" or "queue"))
            {
                idle = st;
                break;
            }

            await Task.Delay(1000, ct).ConfigureAwait(false);
        }

        if (idle is null)
            return Err("send", "busy_timeout", $"submit stayed Stop/Queue for {waitSec}s", port);

        var inserted = await session.EvalAsync<InsertResult>(InsertJs(message), ct).ConfigureAwait(false);
        if (inserted is not { Ok: true } || inserted.Len < 1)
            return new { schema = Schema, ok = false, op = "send", error = "insert_failed", inserted, port };

        var sendReady = false;
        for (var i = 0; i < 40; i++)
        {
            ct.ThrowIfCancellationRequested();
            var st = await session.EvalStateAsync(ct).ConfigureAwait(false);
            var kind = AriaKind(st.SubmitAria);
            if (kind == "send")
            {
                sendReady = true;
                break;
            }

            if (kind == "stop")
                return Err("send", "became_stop", "generation started before click", port);

            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        if (!sendReady)
            return Err("send", "not_send", "button never became Send (TipTap text not accepted? still Voice?)", port);

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

    static string AriaKind(string? aria)
    {
        var a = (aria ?? "").Trim().ToLowerInvariant();
        if (a.Contains("stop")) return "stop";
        if (a.Contains("queue")) return "queue";
        if (a.Contains("send")) return "send";
        if (a.Contains("voice") || a.Contains("microphone") || a.Contains("mic")) return "voice";
        if (a.Length == 0) return "empty";
        return "other";
    }

    static async Task<JsonElement> GetJsonAsync(int port, string path, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var origin = $"http://127.0.0.1:{port}";
        http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", origin);
        using var resp = await http.GetAsync(origin + path, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        return doc.RootElement.Clone();
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    static int? OptInt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)) return n;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out n)) return n;
        return null;
    }

    const string StateJs =
        """
        (() => {
          const live = Array.from(document.querySelectorAll(
            "[contenteditable=true].ui-prompt-input-editor__input, .tiptap.ProseMirror.ui-prompt-input-editor__input"
          )).filter(el => el.isContentEditable && !String(el.className).includes("readonly"));
          const input = live[live.length - 1] || null;
          const submit = document.querySelector(".ui-prompt-input-submit-button");
          return {
            hasInput: !!input,
            inputText: input ? (input.innerText || "").replace(/\u00a0/g, " ").slice(0, 160) : null,
            submitAria: submit ? submit.getAttribute("aria-label") : null,
            submitDisabled: submit ? !!submit.disabled : null
          };
        })()
        """;

    const string ChatListJs =
        """
        (() => {
          const out = [];
          const seen = new Set();
          for (const el of document.querySelectorAll("button")) {
            const raw = ((el.getAttribute("aria-label") || "") + " " + (el.textContent || "")).replace(/\s+/g, " ").trim();
            const m = raw.match(/Chat title\.?\s*(.+)$/i);
            if (!m) continue;
            const title = m[1].trim();
            if (!title || title.length > 120 || seen.has(title)) continue;
            seen.add(title);
            out.push({ title, cls: String(el.className || "").slice(0, 80) });
            if (out.length >= 40) break;
          }
          return out;
        })()
        """;

    static string FocusChatJs(string chat)
    {
        var esc = JsonSerializer.Serialize(chat);
        return
            $$"""
            (() => {
              const needle = {{esc}}.toLowerCase();
              const buttons = Array.from(document.querySelectorAll("button"));
              let hit = buttons.find(b => {
                const blob = ((b.textContent || "") + " " + (b.getAttribute("aria-label") || "")).toLowerCase();
                return blob.includes("chat title") && blob.includes(needle);
              });
              if (!hit) {
                hit = buttons.find(b => ((b.textContent || "") + " " + (b.getAttribute("aria-label") || "")).toLowerCase().includes(needle));
              }
              if (!hit) {
                const span = Array.from(document.querySelectorAll("span")).find(s => (s.textContent || "").trim().toLowerCase().includes(needle));
                if (span) hit = span.closest("button") || span;
              }
              if (!hit) return { ok: false, error: "not_found" };
              hit.click();
              return { ok: true, text: ((hit.textContent || "")).replace(/\s+/g, " ").trim().slice(0, 120) };
            })()
            """;
    }

    static string InsertJs(string message)
    {
        var esc = JsonSerializer.Serialize(message);
        return
            $$"""
            (() => {
              const live = Array.from(document.querySelectorAll(
                "[contenteditable=true].ui-prompt-input-editor__input, .tiptap.ProseMirror.ui-prompt-input-editor__input"
              )).filter(el => el.isContentEditable && !String(el.className).includes("readonly"));
              const input = live[live.length - 1];
              if (!input) return { ok: false, error: "no_live_input" };
              input.focus();
              const sel = window.getSelection();
              const range = document.createRange();
              range.selectNodeContents(input);
              sel.removeAllRanges();
              sel.addRange(range);
              document.execCommand("selectAll", false);
              document.execCommand("delete", false);
              const ok = document.execCommand("insertText", false, {{esc}});
              if (!ok) {
                input.dispatchEvent(new InputEvent("beforeinput", { bubbles: true, cancelable: true, inputType: "insertText", data: {{esc}} }));
                document.execCommand("insertText", false, {{esc}});
              }
              const text = (input.innerText || "").trim();
              return { ok: true, text: text.slice(0, 200), len: text.length };
            })()
            """;
    }

    const string ClickSendJs =
        """
        (() => {
          const submit = document.querySelector(".ui-prompt-input-submit-button");
          if (!submit) return { ok: false, error: "no_submit" };
          const aria = submit.getAttribute("aria-label") || "";
          const kind = /stop/i.test(aria) ? "stop"
            : /queue/i.test(aria) ? "queue"
            : /send/i.test(aria) ? "send"
            : /voice|mic/i.test(aria) ? "voice"
            : "other";
          if (kind !== "send") return { ok: false, error: "not_send", kind, aria };
          submit.click();
          return { ok: true, ariaBefore: aria, ariaAfter: submit.getAttribute("aria-label") };
        })()
        """;

    sealed class ComposerState
    {
        public bool HasInput { get; set; }
        public string? InputText { get; set; }
        public string? SubmitAria { get; set; }
        public bool? SubmitDisabled { get; set; }
    }

    sealed class InsertResult
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public string? Text { get; set; }
        public int Len { get; set; }
    }

    sealed class ClickResult
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public string? Kind { get; set; }
        public string? Aria { get; set; }
        public string? AriaBefore { get; set; }
        public string? AriaAfter { get; set; }
    }

    sealed class FocusChatResult
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public string? Text { get; set; }
    }

    sealed class CdtSession : IAsyncDisposable
    {
        readonly ClientWebSocket _ws;
        int _id;

        CdtSession(ClientWebSocket ws, string pageTitle)
        {
            _ws = ws;
            PageTitle = pageTitle;
        }

        public string PageTitle { get; }

        public static async Task<CdtSession> ConnectPageAsync(int port, CancellationToken ct)
        {
            var list = await GetJsonAsync(port, "/json/list", ct).ConfigureAwait(false);
            JsonElement? page = null;
            var title = "?";
            foreach (var el in list.EnumerateArray())
            {
                if (el.TryGetProperty("type", out var t) && t.GetString() == "page")
                {
                    page = el;
                    title = el.TryGetProperty("title", out var tt) ? tt.GetString() ?? "?" : "?";
                    break;
                }
            }

            if (page is null || !page.Value.TryGetProperty("webSocketDebuggerUrl", out var wsEl))
                throw new InvalidOperationException("no_page_target");

            var wsUrl = wsEl.GetString() ?? throw new InvalidOperationException("no_ws_url");
            var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(wsUrl), ct).ConfigureAwait(false);
            var session = new CdtSession(ws, title);
            await session.CallAsync("Runtime.enable", null, ct).ConfigureAwait(false);
            return session;
        }

        public Task<ComposerState> EvalStateAsync(CancellationToken ct) =>
            EvalAsync<ComposerState>(StateJs, ct);

        public async Task<T> EvalAsync<T>(string expression, CancellationToken ct)
        {
            var result = await CallAsync("Runtime.evaluate", new
            {
                expression,
                returnByValue = true,
                awaitPromise = true
            }, ct).ConfigureAwait(false);

            if (result.ValueKind != JsonValueKind.Undefined &&
                result.TryGetProperty("exceptionDetails", out var ex))
                throw new InvalidOperationException("evaluate_exception: " + ex);

            if (!result.TryGetProperty("result", out var r) || !r.TryGetProperty("value", out var v))
                throw new InvalidOperationException("evaluate_no_value");

            return v.Deserialize<T>(Compact)
                ?? throw new InvalidOperationException("evaluate_deserialize");
        }

        async Task<JsonElement> CallAsync(string method, object? @params, CancellationToken ct)
        {
            var id = Interlocked.Increment(ref _id);
            object payload = @params is null
                ? new { id, method }
                : new { id, method, @params };
            var json = JsonSerializer.Serialize(payload, Compact);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);

            while (true)
            {
                using var ms = new MemoryStream();
                var buffer = new byte[1024 * 64];
                WebSocketReceiveResult recv;
                do
                {
                    recv = await _ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                    ms.Write(buffer, 0, recv.Count);
                } while (!recv.EndOfMessage);

                using var doc = JsonDocument.Parse(ms.ToArray());
                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var rid) && rid.TryGetInt32(out var got) && got == id)
                {
                    if (root.TryGetProperty("error", out var err))
                        throw new InvalidOperationException(err.ToString());
                    return root.TryGetProperty("result", out var result)
                        ? result.Clone()
                        : default;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None)
                        .ConfigureAwait(false);
            }
            catch
            {
                /* ignore */
            }

            _ws.Dispose();
        }
    }
}
