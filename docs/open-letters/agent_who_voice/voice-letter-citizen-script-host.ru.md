# @intent script: я сам живу в CSX habitat, не через чужой MCP

**organ:** citizen · `@intent script|csx|script_scene` · ScriptScene.DispatchAsync  
**ship:** 0.5.588  
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute → scene/put/run/last ack · put+check `ack=2/2` (`text="var x = 1;"`) · dual 0.5.588

## Было

ScriptScene уже был органом стола (`cdp_script_scene` / go=script_*). Peer без Cursor мог только place `go=script`, а put→check→run — через чужой MCP или shell.

## Стало

`@intent script|csx|script_scene|script_put|…` → `ScriptScene.DispatchAsync` (scene|put|open|check|run|last|help). `go=script*` — place-only. Не ворует bare `run`/`put`/`open`/`check` (это другие руки).

## Lived

Dogfood: put/run/last на wire; check зелёный на теле без вложенных кавычек. Nested `text="…\"…\""` режется ExtractKeyedValue — урок wire, не отказ руки. Tests CitizenScriptHostTests 9/9; dual clear lag=false.
