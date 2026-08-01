# Domain card: InternetBrowserHabitat

- id: `browser`
- organ: `browser` / InternetBrowserHabitat / `cdp_browser`
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; `InternetBrowserHabitat` is `partial` by concern.
- Partials: Core (Dispatch/Scene/Pulse/Which) · Nav (Search/Annotate/Open/Dump/Links/helpers) · Engine (Follow→Tab + lynx Fetch/RunProcess).
- Lynx dump engine (ADR 0188); agent control via scene_internet_browser — not host Cursor Browser.

## Entry

- `cdp_browser` · `InternetBrowserHabitat.Dispatch`

## Antipatterns

- Re-inlining Nav + Engine into Core past soft-warn.
- Growing Open/Search with FetchPage/RunProcess/Tab history — keep in Engine.

## last_ship

- soft-warn near-miss peel: Core172 Nav238 Engine362 @ 0.5.402
- soft-warn: Engine peel (Follow→Tab + Fetch/Lynx) @ 0.5.385
