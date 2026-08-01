# Domain card: CdpPluginQuarantine (Mode-A plugins)

- id: `quarantine`
- organ: plugin quarantine under `%LocalAppData%/cdp-mcp/plugins/`
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; `CdpPluginQuarantine` is `partial` by concern.
- Partials: Core (records/List/mutate) · Install (vsix/unpacked) · Groups (attention/reharvest) · Classify (harvest Mode A|B|D) · Host (PATH probe) · Payload (scan/score/helpers).
- Groups = attention filter (enabled plugin ∩ enabled groups); not a second install store.

## Entry

- `CdpPluginQuarantine.List` / `InstallFromVsix` / `InstallFromUnpacked` / `HostProbeCard` / `ReharvestInstalled`

## Antipatterns

- Re-inlining harvest/host/payload into one mega-file past soft-warn.
- Treating group disable as delete of plugin files.

## last_ship

- soft-warn peel: Core250 Install147 Groups371 Classify163 Host143 Payload231 @ 0.5.396
