# Domain card: IdePluginsChannel (go=plugins)

- id: `plugins`
- organ: `go=plugins` / soft organ plugins desk
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400 (ADX soft-warn 350); `IdePluginsChannel` is `partial` by concern.
- Partials: Core (Snap/Handle/Pulse/Publish) · Boards (list/groups enable-assign) · Marketplace (search/install) · Marketplace.Want (feature want + fit helpers) · Preview (plant PNG + arg helpers).
- Quarantine store stays in `CdpPluginQuarantine`; this channel is the desk/Open VSX surface.

## Entry

- `IdePluginsChannel.Handle` / `PulseLine` / `PublishGlass`

## Antipatterns

- Re-inlining Marketplace + Want + Boards + Preview into one mega-file past soft-warn.
- Bypassing quarantine install path for Open VSX installs.

## last_ship

- soft-warn: Want peel → `IdePluginsChannel.Marketplace.Want.cs`; Marketplace 378→235 @ 0.5.423 · 2026-08-01
- soft-warn peel: Core160 Boards257 Marketplace378 Preview187 @ 0.5.399
