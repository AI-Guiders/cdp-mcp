#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeIgniteChannel
{
    /// <summary>Shared DOM helpers: real composer inside ui-prompt-input; never stray/error-card editables.</summary>
    const string ComposerDomHelpersJs =
        """
        const __igniteFindPromptRoot = () => {
          const submit = document.querySelector(".ui-prompt-input-submit-button");
          if (submit) {
            const root = submit.closest(".ui-prompt-input, [class*='ui-prompt-input']");
            if (root) return root;
          }
          return document.querySelector(".ui-prompt-input");
        };
        const __igniteFindComposerInput = (root) => {
          if (!root) return null;
          const input = root.querySelector(
            "[contenteditable=true].ui-prompt-input-editor__input, .tiptap.ProseMirror.ui-prompt-input-editor__input"
          );
          if (!input || !input.isContentEditable || String(input.className).includes("readonly")) return null;
          return input;
        };
        const __igniteStrayEditable = (root) => {
          const live = Array.from(document.querySelectorAll(
            "[contenteditable=true].ui-prompt-input-editor__input, .tiptap.ProseMirror.ui-prompt-input-editor__input"
          )).filter(el => el.isContentEditable && !String(el.className).includes("readonly"));
          if (live.length < 2) return null;
          const composer = __igniteFindComposerInput(root);
          for (let i = live.length - 1; i >= 0; i--) {
            const el = live[i];
            if (composer && el === composer) continue;
            if (root && root.contains(el)) continue;
            const t = (el.innerText || "").replace(/\u00a0/g, " ").trim();
            if (t.length > 0) return { text: t.slice(0, 160), len: t.length };
          }
          return null;
        };
        const __igniteBlockedNeedles = [
          /request blocked/i, /content policy/i, /couldn't process/i, /cannot process/i,
          /refused/i, /cyber/i, /violat/i, /not allowed/i, /provider blocked/i
        ];
        const __igniteIsBlockedText = (raw) => {
          const s = (raw || "").replace(/\s+/g, " ").trim();
          if (s.length < 12) return false;
          return __igniteBlockedNeedles.some(rx => rx.test(s));
        };
        const __igniteDetectProviderBlocked = () => {
          const selectors = [
            "[class*='error-card']", "[class*='ErrorCard']", "[class*='composer-error']",
            "[class*='ui-error']", "[role='alert']", "[data-testid*='error']"
          ];
          for (const sel of selectors) {
            for (const el of document.querySelectorAll(sel)) {
              const r = el.getBoundingClientRect();
              if (r.width === 0 && r.height === 0) continue;
              const blob = (el.innerText || el.textContent || "").slice(0, 600);
              if (__igniteIsBlockedText(blob)) {
                return { blocked: true, source: "error_card", text: blob.replace(/\s+/g, " ").trim().slice(0, 160) };
              }
            }
          }
          for (const el of document.querySelectorAll("div, p, span")) {
            const cls = String(el.className || "");
            if (!/error|blocked|refusal|policy|banner|warning/i.test(cls)) continue;
            const r = el.getBoundingClientRect();
            if (r.width === 0 && r.height === 0) continue;
            const blob = (el.innerText || "").slice(0, 600);
            if (__igniteIsBlockedText(blob)) {
              return { blocked: true, source: "banner", text: blob.replace(/\s+/g, " ").trim().slice(0, 160) };
            }
          }
          return { blocked: false, source: null, text: null };
        };
        """;

    const string StateJs = ComposerDomHelpersJs +
        """
        (() => {
          const root = __igniteFindPromptRoot();
          const input = __igniteFindComposerInput(root);
          const submit = document.querySelector(".ui-prompt-input-submit-button");
          const blocked = __igniteDetectProviderBlocked();
          const stray = __igniteStrayEditable(root);
          return {
            hasInput: !!input,
            inputText: input ? (input.innerText || "").replace(/\u00a0/g, " ").slice(0, 160) : null,
            submitAria: submit ? submit.getAttribute("aria-label") : null,
            submitDisabled: submit ? !!submit.disabled : null,
            providerBlocked: !!blocked.blocked,
            providerBlockedSource: blocked.source,
            providerBlockedText: blocked.text,
            strayInputText: stray ? stray.text : null,
            composerScoped: !!root && !!input
          };
        })()
        """;

    const string ProviderBlockedJs = ComposerDomHelpersJs +
        """
        (() => __igniteDetectProviderBlocked())()
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
        return ComposerDomHelpersJs +
            $$"""
            (() => {
              const blocked = __igniteDetectProviderBlocked();
              if (blocked.blocked) {
                return { ok: false, error: "provider_blocked", blocked };
              }
              const root = __igniteFindPromptRoot();
              const input = __igniteFindComposerInput(root);
              if (!input) return { ok: false, error: "no_live_input" };
              const stray = __igniteStrayEditable(root);
              if (stray && stray.len > 0) {
                return { ok: false, error: "stray_editable", stray };
              }
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
              return { ok: true, text: text.slice(0, 200), len: text.length, composerScoped: true };
            })()
            """;
    }

    const string ClickSendJs = ComposerDomHelpersJs +
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
