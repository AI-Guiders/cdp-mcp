# CDP-ADR-0224: KB AutoShip — FileWatcher in CdpService

**Status:** Accepted  
**Date:** 2026-09-12  
**project-id:** `cdp-mcp` · extends ADR-0213 (wake dispatch), ADR-0207 (writing canon / agent-notes chain)  
**Tags:** #cdp #adr #autoship #git #agent-notes #kb

---

## Context

Agents write KB through `cdp_buffer`, `memory_*`, forum, and external editors. Requiring a manual `git commit && push` after every edit is error-prone and writer-dependent. A tool hook on `memory_*` would miss Cursor Write and other disk paths.

Operator intent (2026-09-07): one `git push origin main` with multi-`pushurl` on origin — not separate pushes per remote.

## Decision

CdpService runs a background **FileSystemWatcher** on configured KB git roots (default: agent-notes repo from `memory.notes_config` chain):

1. **Debounce** — ship after **90s quiet** (configurable `debounce_ms`).
2. **Ignore** — `.git/**` changes do not reset the timer.
3. **Sync gate** — `git fetch origin`; if `origin/<branch>` is ahead of local HEAD → **SA alert, abort** (no auto-rebase / auto-merge).
4. **Commit** — `git add -A` (respect `.gitignore`); message `KB auto: <top paths>`.
5. **Secret gate** — same secret-risk scan as `cdp_ship` (`.env`, credentials, etc.) → alert, no commit.
6. **Push** — `git push origin <branch>`; reject → SA alert via `CideWakeDispatch`.

Lifecycle: static instance in `CdpServiceHost` (same GC-root pattern as `LineWakePoller`), started after `CdpHostRuntime.CreateAsync` loads settings.

Config: `[kb.autoship]` in `cdp-mcp.toml` — `enabled`, `debounce_ms`, `roots[]`, `branch`.

## Non-goals

- Product code repos (only configured KB roots).
- Silent force-push or auto-rebase on markdown canon.
- Replacing `git_plan` / `cdp_ship` for intentional logical slices on handoff.

## Consequences

- KB edits converge to remote within ~debounce after last write without agent ceremony.
- Parallel burst edits coalesce to one commit per root per quiet window.
- Remote-ahead conflicts surface to operator; tree stays untouched until manual reconcile.
