# Domain card: Agent surface (`cdp_glass`)

- id: `surface`
- organ: `cdp_glass` / `go=surface_desk` · Glass `GlassSurfaceCommandHub`
- product: `#CDP` / Glass WPF
- contract: cascade-ide `docs/design/agent-surface-parity-contract-v0.md`

## Invariants

- Second debt = co-presence in human visual channel (not CDP habitat replacement).
- Full DoD: Sense + Aim + Drive (not sense-lite / not webcam).
- IPC = request/reply latches under flat `%LocalAppData%/cdp-mcp` (`surface-cmd-LATEST` / `surface-reply-LATEST`) — not workspace seat.
- Avalonia `UiLayoutSnapshot` is reference wire only; WPF adapter is first host; Qt/C++ later.
- Soft-hide ListTools Meta; CallTool / go= ok.

## Entry

- CDP: `IdeGlassSurfaceChannel` · `GlassSurfaceIpc`
- Glass: `GlassSurfaceCommandHub` · `GlassUiLayoutSnapshot`
- Paths: `CdpHabitatPaths.SurfaceCmdLatch*`

## Antipatterns

- Claiming parity after PrintWindow-only.
- One-way SoftOrgan latch for layout (no reply).
- Pulling Avalonia Visual into Core as SSOT.

## last_ship

- 2026-08-02: Full debt close — `set_control_layout|set_panel_size|request_confirmation` @ 0.5.447; dogfood layout+panel (confirm = human modal, not auto-dogfood)
- 2026-08-02: Aim+Drive — `highlight|focus|click|set_text|send_keys|appearance|colors` @ 0.5.446; dogfood SendBtn highlight + ComposerBox focus
- 2026-08-02: v0 Sense `layout` live — Meta + IPC + WPF walker; dogfood 3 TopLevels
