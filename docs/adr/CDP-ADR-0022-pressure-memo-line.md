# CDP-ADR-0022: Pressure memo line — anti-compaction agent archive

**Status:** accepted  
**Date:** 2026-07-31  
**project-id:** `cascade-ide` · consumer: CDP / Agent Env  
**Tags:** #cdp #adr #pressure #compact #continuity #memo

**Related:** CDP-ADR-0018 (pressure desk) · AutoIgnition amnesia postfix · playbook-context-pressure-checkpoint-v1

---

## Context

Hot pressure stash is last-wins — good for L1 window, weak as a history of flight.
Host compaction is hostile; L1 notify (~2–3 turns before) is the trigger to write a **konspekt**, not a raw transcript.
Oxygen-mask rule: agent continuity first, then operator surfaces (Virtual History).

---

## Decision

1. Append-only `pressure-memo.jsonl` under workspace `StateRoot` (+ `pressure-memo-LATEST.md`).
2. Ops on `cdp_pressure`: `memo` (append) · `line`/`history`/`tail` (last N).
3. `op=stash` and AutoIgnition handoff **auto-append** to the memo line (kind=`stash`|`ignite_handoff`).
4. Dedup identical consecutive body — keep the line lean.
5. Amnesia charge: restore via `op=recall` (hot) · `op=line` (history).
6. Not a replacement for `cdp_learn` (learning cards → promote KB).

---

## Consequences

- Agent owns a compaction-resistant line of memos written the way it needs them.
- Operator Glass Virtual History remains a separate peel (human scroll / PF query-on-demand).

---

## Non-goals (v0)

Auto-summarize full Cursor transcript; topic cards; VDR promotion.

---

## Ship

`IdePressureChannel.Memo` · Meta/ops · domain card · **0.5.318**
