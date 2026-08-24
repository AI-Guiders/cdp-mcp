# CDP-ADR-0204: cdp_peek \u2014 structural outline for large files

- **Status:** Accepted
- **Date:** 2026-08-24
- **Related:** ADR-0201 (cdp_peek) \u00B7 ADR-0031 (Explore corr \u2014 dig) \u00B7 get_document_symbols (Roslyn/LSP)

## Context

`cdp_peek` (ADR-0201) is fast read-only disk ingress with offset/limit pagination. For **large structured files** (docs, KB cards, configs, logs) agents read them in slices \u2014 several `offset/limit` calls, each landing whole in context. On the host that collides with the opencode `tool_output` cap: a big slice is truncated and dumped to `tool-output/`, and the agent must re-dig. Gulp reads of big files are both expensive and truncation-prone.

Meanwhile CDP is **not yet a multilang IDE**: real symbol outline exists only where a language backend is mounted (`get_document_symbols` \u2014 Roslyn/LSP for C#/TS). Dragging an LSP per file type just for an outline is the wrong cost.

## Decision

Add **`mode=outline`** to `cdp_peek` \u2014 a cheap, **LSP-free structural map** for files that carry lightweight structural markers:

| Kind | Ext | Marker | Fields |
|------|-----|--------|--------|
| markdown | `.md`/`.markdown` | ATX headings `#`..`######` | level, name, line, anchor |
| json | `.json`/`.jsonc` | top-level keys / `$root` | name, kind, line (0) |
| yaml | `.yaml`/`.yml` | (reserved) | \u2014 |
| toml | `.toml` | (reserved) | \u2014 |

- Single cheap call returns the whole section map (\u2248 \u2264 40 entries) \u2014 no slice gulps.
- Entries carry `anchor = [F:;L:<line>]`, so the agent jumps straight to a section via `anchor=`/`offset=`.
- For code languages with a mounted backend, outline **delegates to `get_document_symbols`** (no duplicated symbol logic in peek).
- **Auto hint**: on large structured files (>200 lines) the normal `cdp_peek` result also returns `outline_hint` pointing to `mode=outline`, so agents discover the cheaper path.

### Surface

- `mode=outline` (+ `path=`); result: `kind`, `total_lines`, `count`, `entries[{level,name,line,anchor}]`, `supported=false` when no markers.
- Output schema stays `cdp_peek/v1` (new `mode` value + `outline_hint` field).

## Consequences

- **Outline-first** for big files: one `mode=outline` call \u2192 section map \u2192 targeted `offset=` reads. Matches the nested[axb] full-a discipline: cheap closed `a` before targeted `b`.
- Multi-language friendly without CDP pretending to be a multilang IDE: md/json/yaml/toml via markers, code via existing backends.
- Keeps `tool_output` pressure low (no giant slices).
- Stays read-only, sidecar-safe, in-proc (ADR-0200 orthogonal).

## Non-goals (v1)

- Full language-aware symbol outline for every type (defer to `get_document_symbols`/LSP where mounted).
- Line-accurate JSON/YAML keys (marker `line` is 0 until a streaming parser is worth it).
- Write/refactor affordances on top of outline.
