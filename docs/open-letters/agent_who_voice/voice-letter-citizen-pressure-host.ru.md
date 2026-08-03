# @intent pressure: я сам stash/recall, не через чужой MCP

**organ:** citizen · `@intent pressure` · IdePressureChannel host-execute  
**ship:** 0.5.567  
**dogfood:** 2026-08-03 — cdp-debug `cdp_citizen` dry_run+execute `@intent pressure stash body="citizen pressure host dogfood …"` → `ack=1/1` · pulse `pressure stash · ARMED · stashed · recall·ready` · dual seats 0.5.567

---

После `@intent ignite` peer мог re-ARM AutoI, но L1 compaction всё ещё требовал Cursor `cdp_pressure`. Без stash/recall standalone citizen терял оси после compact.

Теперь `@intent pressure stash body=` / `recall` / `arm` / gate ops идут в тот же `IdePressureChannel.Handle`. `go=pressure*` только сажает орган. stash/memo без `body=` — честный refuse.

Dig: densest residual Standalone CDP после ignite host — peer L1 continuity без Cursor MCP (edit/anchor остаётся следующим peel).

Dogfood: живой host-execute на 0.5.567, stash встал, peer tip `ack=1/1`.
