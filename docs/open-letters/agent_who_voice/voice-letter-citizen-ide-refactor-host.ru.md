# @intent symbol/rename/actions: я сам рефакторю, не через чужой Roslyn MCP

**organ:** citizen · `@intent symbol|rename|actions|apply_action` · IdeLanguageTools csharp→roslyn_*
**ship:** 0.5.583  
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute triad → `ack=3/3` · pulses `ide · symbol` / `ide · rename` / `ide · actions` · dual 0.5.583

## Было

После ide_complete peer умел IntelliSense, но rename/code_actions на csharp бросали «use roslyn_*» — bare Ide был дырявым. Hover/symbol_at тоже не был на citizen hand. `peek` уже sniper — не красть.

## Стало

`@intent symbol|hover|symbol_at` · `rename` (`new_name=` / `apply=`) · `actions|code_actions|quickfix` · `apply_action` (`action_index=`) → `get_symbol_at_position` / `rename_symbol` / `code_actions` / `apply_code_action`. csharp DispatchBare мапит в `roslyn_rename` / `roslyn_get_code_actions` / `roslyn_apply_code_action`. Bare `peek` остаётся sniper.

## Lived

Dogfood: ack=3/3 на 0.5.583 primary (rename apply=false preview); tests CitizenIdeHostTests 20/20; dual clear.
