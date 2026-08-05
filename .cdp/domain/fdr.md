# Domain: FDR (Flight Data Recorder)

## Invariants
- Closed flight = `kind=tool_call` with `outcome=ok|error|cancel` (latency for suggest/slow/stats).
- In-flight stamp = `kind=tool_start` / `outcome=running` at CallTool begin — dig ghosts when host aborts without `finally`.
- Open flight = `tool_start` without later closed `tool_call` for same `call_id` → `op=open`.
- FDR/teeth/postmortem desk tools do not pollute the tape.
- timeout_wake suggest/apply uses closed flights only (not starts).

## Entry
- SoftOrgan `go=fdr` / Meta `cdp_fdr` — `op=scene|tail|stats|slow|open|suggest|apply|clear_overlay`
- Wire: `IdeToolCallWatch.RunAsync` → `RecordToolStart` then `RecordToolCall` in `finally`
- Tape: `%StateRoot%/fdr-tape.jsonl`

## Antipatterns
- Digging hangs only via `op=slow` / suggest when finally never ran (ghost) — use `op=open`.
- Counting `tool_start` in p50/p95 latency.

## last_ship
- **2026-08-05 · 0.5.664** — FDR ghost hang dig: `tool_start` at begin + `op=open` + cancel close path; stats/suggest ignore starts.
