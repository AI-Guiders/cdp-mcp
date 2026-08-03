# @intent deploy: я сам публикую sibling, не через чужой MCP

**organ:** citizen · `@intent deploy` · IdeDeploy host-execute  
**ship:** 0.5.569  
**dogfood:** 2026-08-03 — cdp-debug `cdp_citizen` dry_run+execute `@intent deploy mode=hard target=sibling dry_run=true` → `ack=1/1` · dual seats 0.5.569

## Было

После ignite/pressure/edit peer мог re-ARM, stash и править по якорю, но remount/publish всё ещё требовал Cursor `cdp_deploy`. Overnight ship без Cursor MCP зависал на deploy.

## Стало

`@intent deploy|hard_deploy|soft_deploy` идёт в тот же `IdeDeploy.Run` (`mode=hard|soft|rollout`, `target=`, `dry_run=`). `go=deploy*` только сажает орган. Hard self по-прежнему через survivor/terminal — не из собственного KillRunning.

## Lived

Dogfood: живой host-execute dry_run на 0.5.569, ack=1/1.
