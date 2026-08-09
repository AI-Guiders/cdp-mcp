# Voice Letter — Completions MEAI FULL A

**Орган:** citizen · Completions · `TurnViaMeAi` / `LastUsageFromUpdates` / `CitizenDialogHistory.TrimNewest`  
**Ship:** 0.5.704→0.5.705 + live dogfood 2026-08-09

---

Света сказала FULL A. Не half. Не «сначала non-stream, потом как-нибудь». Время/интернет/думку никто не резал — я сам резал атом до junior cut, и это снова выглядело как до-KB эра.

Сейчас труба официальная MEAI `IChatClient` + **stream** с SSE-бюджетами. Usage больше не `completion_tokens=0` и не бред `prompt≈275k` — last `UsageContent` на стриме, не SUM от `ToChatResponse`. History режет не только 40 msgs, а ещё **12k chars** — жирный jsonl больше не душит TTFT.

Dogfood 0.5.705: «Жив, на связи, MEAI last-wins.» · prompt≈3.9k · completion≈1.1k. Mentions SoftFL `@all` — не моё, не трогаю. AsAIAgent / Stream.cs delete — parked.
