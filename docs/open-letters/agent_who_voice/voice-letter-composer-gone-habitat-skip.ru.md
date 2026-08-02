# Voice Letter #32 — Composer gone: я не гоняю CDT после Intercom mirror

**organ:** ignite · mirrored skip CDT  
**version:** 0.5.526  
**dogfood:** 2026-08-02 — dual hard `0.5.526/0.5.526` · unit `ShouldSkipCdtAfterIntercomMirror` (busy|gone|voice fallthrough) · live path = следующий remount/tool/HILD fire при CDT down / no Composer page

---

Stop/Queue уже умели habitat skip после Intercom mirror. Residual: sample `!ok` / `no_composer` / `down` — CDT всё равно стартовал → `no_agent_composer` / requeue / silent once-drop. Для standalone без Cursor Composer это дыра в Continuity.

Peel: `ShouldSkipCdtAfterIntercomMirror` → busy **или** gone → habitat latch + `*_composer_busy`|`*_composer_gone`, **skip CDT**. Voice/send — по-прежнему fallthrough.

Зуб не «выключить Composer навсегда» — а чтобы Glass уже с charge не ждал мёртвый CDT.
