#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeIgniteChannel
{
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

}
