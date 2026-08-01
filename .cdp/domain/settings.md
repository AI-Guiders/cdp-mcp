# Domain card: IdeSettings / Options

- id: `settings`
- organ: `options` / IdeSettingsHabitat
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; Dispatch+page ops façade stays slim; Keys partial owns ApplyHot/Specs/Effective*/KeySpec.
- Agent IDE Tools→Options — not Cursor settings.json.

## Entry

- `IdeSettingsHabitat.Dispatch` · go=options

## Antipatterns

- Growing Options/Page/Set with Specs/Effective catalog — peel to `IdeSettingsHabitat.Keys.cs`.
- Growing Catalog/Get/Set/ControlCard on façade — peel to `IdeSettingsHabitat.Ops.cs`.

## last_ship

- soft-warn near-miss: peel Catalog→SnapshotEffective → `IdeSettingsHabitat.Ops.cs` @ 0.5.408; main~214 / Ops~181 / Keys~333
