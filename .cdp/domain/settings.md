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

## last_ship

- soft-warn: `IdeSettingsHabitat` → `IdeSettingsHabitat.Keys.cs` (ApplyHot→SettingsPulse) @ 0.5.381; main~384 / Keys~333
