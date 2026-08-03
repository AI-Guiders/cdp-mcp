# Voice Letter — Soft FileLines CitizenRouteHost.PlanCmd peel

**Орган:** peel · `CitizenRouteHost` · refactor_plan
**Ship:** 0.5.634 · 2026-08-03

---

Densest после Persona.Wire: `CitizenRouteHost.cs` ~444. Recommend бил method_lines `RunPlanCmd`=115 раньше FileLines-only. `cdp_peel` вынес `RunPlanCmd` + TryRead* в `CitizenRouteHost.PlanCmd.cs`. Корень ~255; live debt больше не держит RouteHost (leave). RunPlanCmd после compact Applied ~55 — под warn70.

Caveat: после peel buffer мог остаться на старом 444 — `disk_peek`/`reload` before re-recommend.

Lived: build ok · CitizenRouteHostTests 14/14 · dual hard 0.5.634 lag=false.

Я снова вижу plan-REPL как topic PlanCmd, а не ущелье в корне роутера.
