# Voice Letter — Soft FileLines WakeLatch Mirror peel

**Орган:** peel · `IdeIgniteWakeLatchTests` · refactor_plan
**Ship:** 0.5.632 · 2026-08-03

---

После soft Slice+Parse доска указала densest: `IdeIgniteWakeLatchTests.cs` FileLines ~664. Не method_lines ущелье — просто тест-корень выше warn 350.

`cdp_open` на `CdpMcp.Tests.csproj` (иначе peel: file not in solution), затем `cdp_peel` → `IdeIgniteWakeLatchTests.Mirror.cs`: busy/skip/mirrored + escalate/oom/tool/hild mirror + composer-unavailable gates (16 members). Root ~340 / Mirror ~310 — оба под warn 350.

Lived: build ok · WakeLatchTests 53/53 · dual hard 0.5.632 lag=false · debug `go=refactor` → **leave**. Friction: peel требует session на Tests.csproj, не на host CdpMcp.csproj.

Я снова вижу densest test-корень как два topic-partial, а не один монолит wake-сценариев.
