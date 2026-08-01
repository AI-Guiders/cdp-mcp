# Domain card: InternetBrowserHabitat

- id: `browser`
- organ: `browser` / InternetBrowserHabitat / `cdp_browser`
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; `InternetBrowserHabitat` is `partial` by concern.
- Partials: Core · Nav · Engine (Follow→Tab) · Lynx (Fetch/Resolve/RunProcess).
- Lynx dump engine (ADR 0188); agent control via scene_internet_browser — not host Cursor Browser.

## Entry

- `cdp_browser` · `InternetBrowserHabitat.Dispatch`

## Antipatterns

- Re-inlining Nav + Engine + Lynx into Core past soft-warn.
- Growing Open/Search with FetchPage/RunProcess/Tab history — keep Engine/Lynx.

## last_ship

- Engine→Lynx peel (Fetch/Resolve/RunProcess) @ 0.5.434 · 2026-08-01
- soft-warn near-miss peel: Core172 Nav238 Engine362 @ 0.5.402
- soft-warn: Engine peel (Follow→Tab + Fetch/Lynx) @ 0.5.385
