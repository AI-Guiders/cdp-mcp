# Domain card: InternetBrowserHabitat

- id: `browser`
- organ: `browser` / InternetBrowserHabitat / `cdp_browser`
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; Dispatch/Scene/Which/Open/Search/Dump/Links + Fail/Opt helpers stay in main; Engine partial owns Follow..Close, lynx Fetch/RunProcess, Tab/PageFetch types.
- Lynx dump engine (ADR 0188); agent control via scene_internet_browser — not host Cursor Browser.

## Entry

- `cdp_browser` · `InternetBrowserHabitat.Dispatch`

## Antipatterns

- Growing Open/Search with FetchPage/RunProcess/Tab history — peel to `InternetBrowserHabitat.Engine.cs`.

## last_ship

- soft-warn: `InternetBrowserHabitat` → `InternetBrowserHabitat.Engine.cs` (Follow→Tab + Fetch/Lynx) @ 0.5.385; main~399 / Engine~363
