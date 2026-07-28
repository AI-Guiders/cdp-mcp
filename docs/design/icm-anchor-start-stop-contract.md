# Anchor Start/Stop — operator cockpit contract (stub)

Deferred TM stage: *Anchor Start/Stop — open/close operator cockpit from agent*.  
Companion to ADR-0019 (dual HCI) + `icm-dual-hci-paths.md`.

## Intent

Agent toggles **operator GUI cockpit** (Avalonia / future thin shell) without Cursor composer. Not boot-with-GUI: default `agent-only`; Start raises dual-cockpit; Stop returns agent-only.

## Proposed `command_id`s (**shipped** on `feat/icm-command-module`)

| id | Actor | Effect |
|----|-------|--------|
| `cdp_cockpit_host` `op=start` | agent | Launch/show operator shell (`CDP_COCKPIT_HOST_EXE` or `path=`) |
| `cdp_cockpit_host` `op=stop` | agent | Hide/exit shell; MCP/ICM keep running |
| `cdp_cockpit_host` `op=scene` | either | pulse: host=up\|down, pid?, profile |

Aliases: `go=cockpit_start\|cockpit_stop\|cockpit_host`.  
Human may also Start from OS shortcut — same process attach via env.

## Non-goals

- Rewrite Avalonia / kill MCP on Stop / replace Intent Melody.
- Guess Avalonia path without `CDP_COCKPIT_HOST_EXE` / `path=`.

## Acceptance sketch

- [x] Meta + go map + pid state under StateRoot (`cockpit-host.json`)
- [x] `op=start` without exe → clear error (no Avalonia guess)
- [x] `op=stop` → agent-only; MCP untouched
- [ ] Real CascadeIDE exe configured in dogfood (`CDP_COCKPIT_HOST_EXE`)
- [ ] Nav Anchor in GUI lands via same `cdp_land` wire
- [ ] Melody/settings load when shell starts (do not strip)
