# Domain card: IdePluginsChannel (go=plugins)

- id: `plugins`
- organ: `go=plugins` / soft organ plugins desk
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; `IdePluginsChannel` is `partial` by concern.
- Partials: Core (Snap/Handle/Pulse/Publish) · Boards (list/groups enable-assign) · Marketplace (search/install/want) · Preview (plant PNG + arg helpers).
- Quarantine store stays in `CdpPluginQuarantine`; this channel is the desk/Open VSX surface.

## Entry

- `IdePluginsChannel.Handle` / `PulseLine` / `PublishGlass`

## Antipatterns

- Re-inlining Marketplace + Boards + Preview into one mega-file past soft-warn.
- Bypassing quarantine install path for Open VSX installs.

## last_ship

- soft-warn peel: Core160 Boards257 Marketplace378 Preview187 @ 0.5.399
