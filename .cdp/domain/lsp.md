# Domain card: LSP Options

- id: `lsp`
- organ: `options` / LspOptionsToolkit
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; LanguagesPage/Probe/Ensure/Install/Add façade stays slim; Recipes partial owns InstallCore/presets/Recipe catalog.
- missing → browser search → shell install → probe → hot-reload pool.

## Entry

- `LspOptionsToolkit` via IdeSettingsHabitat lsp_* ops

## Antipatterns

- Growing Ensure/Install with Recipe vias / MergePresets — peel to `LspOptionsToolkit.Recipes.cs`.

## last_ship

- soft-warn: `LspOptionsToolkit` → `LspOptionsToolkit.Recipes.cs` (InstallCore→Recipes) @ 0.5.382; main~282 / Recipes~351
