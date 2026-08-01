# Domain card: Glass 0-sync (dual-HCI)

- id: `glass`
- organs: WPF `CDP.GlassCockpit.Windows` · Avalonia projectors · CDP latches
- product: `#CIDE` / `#CDP`
- learn: `note-0-sync-glass-entity-parity` — every CDP desk entity needs CIDE/Glass presence (stub OK)

## Invariants

- CDP habitat = SSOT; Glass = projector (ADR-0021 Windows-first WPF).
- SoftOrgan chrome band ≠ EICAS: alert/qrh/ecl stay EICAS (not SoftOrganLatchCatalog).
- SoftOrganLatchCatalog gates SoftOrgan `*-LATEST.json`; LatchHub routes alert/qrh/ecl separately.
- CabinGlassProjectionCatalog: every SoftOrganKind go-pin resolves (MfdPage or chrome_hint stub).
- Host start hydration (`CockpitHostLatchHydration`) must include SoftOrgan + EICAS latch names that exist on disk.

## Entry

- WPF: `LatchHub` · `EicasBandAggregator` · `LatchPaint.Eicas`
- Avalonia: `CdpEclProjector` · alert/qrh projectors
- CDP: `Cide*Latch` · `CabinGlassProjectionCatalog` · `CockpitHostLatchHydration`

## Antipatterns

- Stuffing alert/qrh into SoftOrgan band (tests explicitly ignore as EICAS bleed).
- Soft deploy ≠ remounted habitat; Glass WPF rebuild is separate from cdp-mcp seat.

## last_ship

- 2026-08-01: WPF Glass ecl-LATEST EICAS wire (Avalonia parity) · cascade-ide `08cc8f34`
- 0.5.364: hydrate domain + sa-desk on cockpit host start
- 0.5.365: `arch_desk` → MFD `SemanticMap` (+ chrome hint) in CabinGlassProjectionCatalog
- 0.5.366: `domain`/`learn` → MFD `MarkdownPreview` (+ chrome hints) — domain cards & learn notes are md
