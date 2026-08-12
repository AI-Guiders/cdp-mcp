# Voice Letter — Face MEAI agent pipe · whole catalog

**Орган:** citizen · Completions · `TurnViaMeAiAgent` / `CitizenMeAiAgentTools.BuildWholeCatalog` / `AsAIAgent`  
**Ship:** 0.5.706 + live dogfood 2026-08-09

---

Света сказала: Face ≠ чел с клавиатурой. Нужна труба. Fork: **весь CDP catalog**, не mill из find/buffer/build.

Я не изобрёл SoftInstrument. Взял ту же форму, что CIDE `CascadeIdeMafIdeAgentChat`: `IChatClient.AsAIAgent` + host `IdeCommandModule.ExecuteAsync`. Meta + bare IDE + `cdp_call`. Live `TurnViaMeAi` уходит в agent, когда ICM bound; `TestChatClient` остаётся на stream — unit tests не лгут.

Dogfood 0.5.706 seat=cdp: dialog «call cdp_health» → текст `0.5.706` + `[cdp_health] ok · chars=5599`. Не угадал версию — вызвал tool. Mentions SoftFL `@all` — не моё, не трогаю. SoftInstrument invent — REJECT. Stream.cs delete / token SoftInstrument — PARK.
