# CDP-ADR-0232: Cursor SDK local Autoi primary (L6)

**Status:** Accepted  
**Date:** 2026-09-19  
**project-id:** `cdp-mcp`  
**Tags:** #cdp #adr #ignite #autoi #cursor-sdk #habitat #axb

**Related:** CDP-ADR-0025 CDT Composer adapter · Habitat AxB L5 same-wake ground · CDP-ADR-0231

---

## Context

Cursor-seat Autoi (`harness=cursor`) historically fired only through CDT Composer (:9222). That couples wake delivery to Electron UI state (Stop/Queue, Connection Problems) and blocks headless/SDK-first automation.

## Decision

**L6:** `@cursor/sdk` local `Agent.create|resume` + `agent.send` is the **primary** Autoi gun when `CURSOR_API_KEY` is configured and the arm does not force CDT.

**CDT Composer remains escape** — `delivery_path=cdt_escape` when SDK is unavailable, unconfigured, or `force_cdt` / `fire_policy=composer|cdt`.

### delivery_path enum

| Path | Meaning |
|------|---------|
| `sdk_local` | Node bridge `scripts/cursor-sdk-local-fire.mjs` delivered charge |
| `cdt_escape` | Fell through to legacy CDT→Composer inject |

### Wake latch

Successful SDK fire publishes `ignite-wake-LATEST.json` with `channel=sdk_local` and optional `sdk_agent_id` for resume.

### Connection watch

`sdk_local` submit kind is habitat-like — no post-fire Connection Problems watch.

## Verification

`IdeIgniteSdkLocalFireTests` — force_cdt, bridge ok, unavailable fallthrough, IsHabitatSubmitKind.
