# CDP-ADR-0026: Citizen AI keys foundation (`ai-keys.toml`)

**Status:** accepted (foundation; loader 0.5.329; host consume via `CitizenCompletions` / `cdp_citizen` — shipped)  
**Date:** 2026-07-31  
**Tags:** #cdp #adr #citizen #secrets #ai-keys

**Related:** CIDE [ADR 0028](../../cascade-ide/docs/adr/0028-user-settings-toml-localappdata-and-secrets.md) (`ai-keys.toml`) · CDP-ADR-0025 (citizen/guest isolation) · [citizen-agent-wire-v0.md](../design/citizen-agent-wire-v0.md)

**Naming:** informal “api-keys.toml” → **canonical `ai-keys.toml`** (already in CIDE). Do not invent a second filename.

---

## Context

Citizen completions need provider keys without putting them in `settings.toml` or MCP env dumps.  
CIDE already stores Anthropic/OpenAI/DeepSeek in `%LocalAppData%\CascadeIDE\ai-keys.toml` (`AiKeys` / `AiKeysStorage`).  
Loader + host path consume the same file in-proc (`CitizenAiKeys` → `CitizenCompletions` / desk `cdp_citizen`).

---

## Decision

### 1. Paths

| Consumer | Path |
|----------|------|
| CIDE / GlassCore settings reuse | `%LocalAppData%\CascadeIDE\ai-keys.toml` (existing) |
| CDP citizen host | **Same file** first (one operator machine, one keyring). Optional later: `%LocalAppData%\cdp-mcp\ai-keys.toml` only if seat isolation requires split. |

### 2. Format

- TOML, snake_case keys, Tomlyn — same stack as CIDE ADR 0028.
- Model fields (v0 align with CIDE `AiKeys`):
  - `anthropic_api_key`
  - `open_ai_api_key`
  - `deep_seek_api_key`
- Extensible later (`cloud_ru_*`, OpenRouter, …) without moving file.
- OpenAI-compat extras (v0.5.360+): `open_ai_base_url`, `open_ai_model` — defaults to Cloud.ru FM when key set and fields empty.

### 3. Security

- Never in `settings.toml`, never in repo, never in Intercom latch JSON, never in pressure stash body.
- Guest MCP must not echo keys in tool results (`ToPublicPulse` / `Masked` only).
- Citizen host loads keys in-proc; guest continues to use Cursor/provider UI for Cursor agents.

### 4. Readiness

| Item | Status |
|------|--------|
| CIDE `ai-keys.toml` + UI/storage | **Ready** (ADR 0028) |
| Citizen loader | **Shipped** (`CitizenAiKeys`, 0.5.329) |
| Citizen host consume | **Shipped** (`CitizenCompletions` + `IdeCitizenChannel` / `cdp_citizen` op=keys|scene|turn) |
| Example template in docs | `docs/design/ai-keys.example.toml` (placeholders only) |

---

## Consequences

- “Are we ready for api-keys?” → **yes for citizen host** when `open_ai_api_key` (Cloud.ru FM / OAI-compat) **or** `anthropic_api_key` is set in CascadeIDE `ai-keys.toml`; otherwise invite blocked / `keys_missing` on live turn (dry_run still free).
- Avoid renaming to `api-keys.toml` — breaks existing CIDE installs.

## Non-goals

- OS secret store / DPAPI (later peel).
- Per-seat split keyring (only if isolation requires it).