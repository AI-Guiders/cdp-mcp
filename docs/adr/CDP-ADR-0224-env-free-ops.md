# CDP-ADR-0224: Env-free ops — zero operator env, CLI + EmbeddedTOML only

**Status:** Accepted (2026-09-12)
**Extends:** ADR-0222 (config TOML), ADR-0223 (ship slot port)

## Decision

**Оператор не задаёт env для CDP.** Seat policy = EmbeddedTOML + operator overlay. Remount, deploy pin, ignite seat = **CLI args** или **derive из пути/ payload**, не переменные окружения.

| Было (env) | Стало |
|---|---|
| `CDP_SERVICE_*` | TOML `[bridge]`/`[slots]` (0222 hard-fail) |
| `CDP_CITIZEN_ENABLED` | `[citizen].enabled` + embedded default |
| `CDP_MCP_CONFIG` | `--config PATH` (Install seeds args); embedded if file missing |
| `CDP_RELOAD_NUDGE` | `--bridge-rev STAMP` in `mcp.json` **args** |
| `CDP_SLOT_PORT` | `--slot-port N` on `CdpService --service` |
| `CDP_IGNITE_SEAT` | `IdeIgniteSeatContext` / durable-job `IgniteSeat` field / `--ignite-notify --seat` |
| `CDP_WORKSPACE_KEY` | derive from `--config` seat dir (`cdp` / `cdp-debug`) |

## Cursor remount

Cursor перезапускает stdio MCP при изменении `command`/`args`/`env` в `mcp.json`. **`--bridge-rev`** — opaque stamp в args; bridge его игнорирует. `CdpBridgeRevNudge` / `CdpReloadNudge.ps1` бампают rev per-seat.

Legacy `CDP_RELOAD_NUDGE` в env: удалять при migrate; bump переводит на args.

## Anti-patterns

- Env для seat policy или remount nudge
- Global regex replace всех nudge keys (pre-0.5.661 thrash)
