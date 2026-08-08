# Domain card: InternetBrowserHabitat

- id: `browser`
- organ: `browser` / InternetBrowserHabitat / `cdp_browser`
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; `InternetBrowserHabitat` is `partial` by concern.
- Partials: Core · Nav · Engine (Follow→Tab) · Lynx (Fetch/Resolve/RunProcess).
- Lynx dump engine (ADR 0188); agent control via scene_internet_browser — not host Cursor Browser.
- **Dual mode:** peer dig DEFAULT (`open|search` → lynx pulse, no PlaceOrgan/Face steal) · Face show OPT-IN (`show` / `face=true` / `to=operator` → `web_ai_url` + Glass WebAiPortal). Operator and citizen may look at different pages.

## Entry

- `cdp_browser` · `InternetBrowserHabitat.Dispatch`

## Antipatterns

- Re-inlining Nav + Engine + Lynx into Core past soft-warn.
- Growing Open/Search with FetchPage/RunProcess/Tab history — keep Engine/Lynx.

## last_ship

- **2026-08-08 browser peer vs Face dual-mode SoftFL** — lived: every `@intent browser open|search` PlaceOrgan stole Glass Face. Ship: default peer dig (lynx only); Face latch via `browser show` / `face=true` / `to=operator` (Citizen + `cdp_browser`). Tests CitizenBrowserHostTests **11/11** (+ dialog persona). SoftFL invent ACCEPT (operator-named).
- Dialog Radio body-map: `@intent browser` taught in DialogSystemPrompt (parity Wire) @ 2026-08-08 — SoftFL for «нет браузера» lie
- Engine→Lynx peel (Fetch/Resolve/RunProcess) @ 0.5.434 · 2026-08-01
- soft-warn near-miss peel: Core172 Nav238 Engine362 @ 0.5.402
- soft-warn: Engine peel (Follow→Tab + Fetch/Lynx) @ 0.5.385
