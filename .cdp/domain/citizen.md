# Domain card: Citizen host (cdp_citizen)

- id: `citizen`
- organ: `cdp_citizen` / `IdeCitizenChannel` + `CitizenCompletions` (+ `CitizenCompletions.OpenAiCompat`)
- product: `#CDP`
- ADR: 0026 / 0028 (citizen completions host)

## Invariants

- Live invite needs `open_ai_api_key` **or** `anthropic_api_key` in `%LocalAppData%/CascadeIDE/ai-keys.toml`.
- Prefer **OpenAI-compat** when `open_ai` key set (Cloud.ru FM); else Anthropic.
- Defaults when keys omit URL/model: `https://foundation-models.api.cloud.ru/v1` · `ai-sage/GigaChat3-10B-A1.8B`.
- Wire: Bearer + `{base}/v1/chat/completions` (non-stream for citizen turns); system-as-message on OAI path.
- `invite_ready` is a **record** (not ValueTuple) — JSON must expose Ready/Status/Checklist/Blocker.
- `dry_run=true` builds persona+wire messages without provider; works with empty keys.
- Dry-run **model** label mirrors live `ResolveProvider` (FM-first / `DefaultOpenAiModel`), not raw `DefaultModel` (claude).
- Soft deploy ≠ live code; hard-self for this seat needs **terminal_*** + KillRunning (not in-proc `cdp_shell_*`).

## Entry

- `cdp_citizen` op=`scene|keys|turn`
- Keys: `CitizenAiKeys` · Completions: `CitizenCompletions*`
- Example: `docs/design/ai-keys.example.toml`

## Antipatterns

- Starting dogfood from social/speech hubs — citizen is completions host, not CASA speech.
- Expecting live turn with empty `ai-keys.toml` (file may exist and still block).
- Treating soft-staged `.next` as remounted live seat.
- Committing real API keys.

## last_ship

- 2026-08-01 → **0.5.442 live**: persona HARD WIRE OUTPUT CONTRACT + OpenAI-compat `temperature=0`. Forced ONLY `@intent go=plan` → exact wire line, `wire_intents`+routes ok on GigaChat3-10B.
- Prior soft persona failed wire (prose / empty intents) — fixed by hard contract + temp=0.
- 0.5.360: Cloud.ru FM OpenAI-compat path + AiKeys helpers
- 0.5.361: `InviteReady` as serializable record
- 0.5.362: meta docs OAI/Cloud.ru on `cdp_citizen`
- 2026-08-01 earlier: dogfood had `http_402` Not enough money — billing later cleared for smoke
