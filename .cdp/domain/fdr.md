# Domain: FDR (Flight Data Recorder)

## Invariants
- Closed flight = `kind=tool_call` with `outcome=ok|error|cancel` (latency for suggest/slow/stats).
- Dynamics = `tool_start` (takeoff) + `tool_tick` mid-flight samples + closed `tool_call` (landing).
- Open flight = `tool_start` without later closed `tool_call` for same `call_id` → `op=open` (includes `last_tick_ms` / `ticks` when present).
- Full trail = `op=trace call=` — start + ticks + close for crash dig.
- FDR/teeth/postmortem desk tools do not pollute the tape.
- timeout_wake suggest/apply uses closed flights only (not starts/ticks).
- Default tick period = 5s (`IdeToolCallWatch.DefaultTickSeconds`).

## Entry
- SoftOrgan `go=fdr` / Meta `cdp_fdr` — `op=scene|tail|stats|slow|open|trace|suggest|apply|clear_overlay`
- Wire: `IdeToolCallWatch.RunAsync` → `RecordToolStart` → periodic `RecordToolTick` → `RecordToolCall` in `finally`
- Tape: `%StateRoot%/fdr-tape.jsonl`

## Antipatterns
- Digging hangs only via `op=slow` / suggest when finally never ran (ghost) — use `op=open` / `op=trace`.
- Counting `tool_start` / `tool_tick` in p50/p95 latency.
- Treating takeoff+landing alone as FDR — without ticks there is no trajectory for crash investigation.

## last_ship
- **2026-08-05 · 0.5.665** — Mid-flight `tool_tick` dynamics + `op=trace`; open carries last tick; stats still closed-only.
- **2026-08-05 · 0.5.664** — FDR ghost hang dig: `tool_start` at begin + `op=open` + cancel close path; stats/suggest ignore starts.
