# CDP-ADR-0026: Citizen AI keys foundation (`ai-keys.toml`)

**Status:** accepted (foundation; in-proc loader shipped 0.5.329 — unused until citizen completions host)  
**Date:** 2026-07-31  
**Tags:** #cdp #adr #citizen #secrets #ai-keys

**Related:** CIDE [ADR 0028](../../cascade-ide/docs/adr/0028-user-settings-toml-localappdata-and-secrets.md) (`ai-keys.toml`) · CDP-ADR-0025 (citizen/guest isolation) · [citizen-agent-wire-v0.md](../design/citizen-agent-wire-v0.md)

**Naming:** informal “api-keys.toml” → **canonical `ai-keys.toml`** (already in CIDE). Do not invent a second filename.

---

## Context

Citizen completions need provider keys without putting them in `settings.toml` or MCP env dumps.  
CIDE already stores Anthropic/OpenAI/DeepSeek in `%LocalAppData%\CascadeIDE\ai-keys.toml` (`AiKeys` / `AiKeysStorage`).  
Citizen host is not shipped; we lock the **contract** before code thrash.

---

## Decision

### 1. Paths

| Consumer | Path |
|----------|------|
| CIDE / GlassCore settings reuse | `%LocalAppData%\CascadeIDE\ai-keys.toml` (existing) |
| CDP citizen host (when shipped) | Prefer **same file** first (one operator machine, one keyring). Optional later: `%LocalAppData%\cdp-mcp\ai-keys.toml` only if seat isolation requires split. |

### 2. Format

- TOML, snake_case keys, Tomlyn — same stack as CIDE ADR 0028.
- Model fields (v0 align with CIDE `AiKeys`):
  - `anthropic_api_key`
  - `open_ai_api_key`
  - `deep_seek_api_key`
- Extensible later (`cloud_ru_*`, OpenRouter, …) without moving file.

### 3. Security

- Never in `settings.toml`, never in repo, never in Intercom latch JSON, never in pressure stash body.
- Guest MCP must not echo keys in tool results.
- Citizen host loads keys in-proc; guest continues to use Cursor/provider UI until citizen ships.

### 4. Readiness

| Item | Status |
|------|--------|
| CIDE `ai-keys.toml` + UI/storage | **Ready** (ADR 0028) |
| Citizen completions host reader | **Loader ready** (`CitizenAiKeys`, 0.5.329) — host path still open |
| Example template in docs | `docs/design/ai-keys.example.toml` (placeholders only) |

---

## Consequences

- “Are we ready for api-keys?” → **keys file line yes for CIDE; citizen consume no until host.**
- Avoid renaming to `api-keys.toml` — breaks existing CIDE installs.

## Non-goals

- Implementing citizen HTTP client in this ADR.
- OS secret store / DPAPI (later peel).
