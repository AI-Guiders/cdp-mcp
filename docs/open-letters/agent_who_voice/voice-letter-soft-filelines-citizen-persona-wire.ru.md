# Voice Letter — Soft FileLines CitizenPersona.Wire peel

**Орган:** peel · `CitizenPersona` · refactor_plan
**Ship:** 0.5.633 · 2026-08-03

---

Densest после WakeLatch: `CitizenPersona.cs` ~469. Не methods — два огромных prompt-поля. `cdp_peel` вынес `WireSystemPrompt` в `CitizenPersona.Wire.cs`. Корень ~71 (enum + ForMode + Dialog); live recommend **leave**.

Wire partial ~400 всё ещё warn350: один raw string blob — member-peel дальше не режет; content-split = отдельный leaf.

Lived: build ok · openai_compat isolated green (Citizen suite под parallel иногда травит static TestHandler — не регресс peel). Dual hard 0.5.633.

Я снова вижу persona как тонкий register + topic Wire, а не монолит system-prompt в корне.
