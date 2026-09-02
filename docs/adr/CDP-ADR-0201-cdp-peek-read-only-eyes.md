# CDP-ADR-0201: cdp_peek — read-only file eyes

- **Status:** Accepted
- **Date:** 2026-08-21
- **Related:** ADR-0198 (sidecar) · ADR-0031 (Explore corr — peek is dig, not mutate) · host Read/Grep

## Context

In CDP habitat, agents used host **Read** and **Grep** for recon while **cdp_buffer** carried mutate semantics (open, diagnostics, Explore corr gates, buffer park). That split was slow and cognitively noisy:

- Read bypasses CDP session roots and anchor wire discipline.
- `cdp_buffer op=read` opens the buffer plane — heavier than a glance.
- `disk_peek` compares drifted buffers vs disk — not general file ingress.
- Find hits land via buffer open (`FindInFiles.TryLand`) — side effects agents don't want during Explore.

We want **eyes as good as Read** inside CDP: fast, paginated, batch-capable, anchor-native — without pretending to be mutate.

## Decision

Add Meta tool **`cdp_peek`** — read-only disk ingress:

| Concern | cdp_peek | cdp_buffer | host Read |
|--------|----------|------------|-----------|
| Opens buffer | no | yes | n/a |
| Explore corr mutate gate | no (dig) | on edit/create | n/a |
| Diagnostics / Roslyn | no | yes | no |
| Anchors `[F:;L:;]` | yes | on edit | no |
| Pagination | offset/limit, next_offset | take/read spans | offset/limit |
| Batch | paths[] ≤8 | multi open | one file |
| Find+peek | query= → rg windows | find opens buffer | Grep only |

### Surface

- **Single:** `path=` + `offset`/`limit` (Read semantics: negative offset from EOF).
- **Land:** `anchor=` / `at=` ± `pad` (default 20).
- **Batch:** `paths[]` with shared char budget (48k).
- **Find+peek:** `query=` + optional `glob=` / `scope=` → up to `max` rg hits with peek windows (disk-only, no buffer open).
- **Lazy bind:** `bind=true` (default) detects project from path when session has no root (lightweight — no list_changed / buffer park).

### Output

Schema `cdp_peek/v1`:

- Numbered **`text`** block (`     1|line`) for LLM-friendly reading.
- Structured **`lines[]`** with `n`, `text`, `anchor` for sniper/edit chain.
- **`has_more`**, **`next_offset`**, **`total_lines`** for pagination.
- Image/binary guards with hints (`cdp_see`, shell).

### Desk aliases

- `go=file_peek` · `go=eyes` · `go=cdp_peek` → Meta tool (sniper `peek` unchanged).

## Consequences

- Explore phase default read path: **`cdp_peek`** → **`find_in_files`/`cdp_search`** → **`cdp_buffer`** only when editing.
- Sidecar-safe: in-proc file read, no tenant coupling (ADR-0200 orthogonal).
- Agents in CDP should prefer `cdp_peek` over host Read — same ergonomics, habitat-native anchors.
- `disk_peek` remains for buffer drift hygiene; not replaced.

## Non-goals (v1)

- Image inlining (use `cdp_see`).
- Writable paths / buffer lifecycle.
- Semantic/FTS index (future `cdp_search what=index`).
