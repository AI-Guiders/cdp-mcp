# Anchor Start/Stop — operator cockpit contract (stub)

Deferred TM stage: *Anchor Start/Stop — open/close operator cockpit from agent*.  
Companion to ADR-0019 (dual HCI) + `icm-dual-hci-paths.md`.

## Intent

Agent toggles **operator GUI cockpit** (Avalonia / future thin shell) without Cursor composer. Not boot-with-GUI: default `agent-only`; Start raises dual-cockpit; Stop returns agent-only.

## Proposed `command_id`s (**shipped** on `feat/icm-command-module`)

| id | Actor | Effect |
|----|-------|--------|
| `cdp_cockpit_host` `op=start` | agent | Launch/show operator shell (`[cockpit_host] exe` / `path=` / env escape) |
| `cdp_cockpit_host` `op=stop` | agent | Hide/exit shell; MCP/ICM keep running |
| `cdp_cockpit_host` `op=scene` | either | pulse: host=up\|down, pid?, profile |

Aliases: `go=cockpit_start\|cockpit_stop\|cockpit_host`.  
Human may also Start from OS shortcut — same process attach via env.

## Non-goals

- Rewrite Avalonia / kill MCP on Stop / replace Intent Melody.
- Guess Avalonia path without toml / `path=` / env escape.

## Acceptance sketch

- [x] Meta + go map; runtime latch in-proc + OS rediscover by exe (no `cockpit-host.json`)
- [x] `op=start` without exe → clear error (no Avalonia guess)
- [x] `op=stop` → agent-only; MCP untouched
- [x] Config SSOT: `[cockpit_host] exe` in `cdp-mcp.toml`; `path=` one-shot; env escape only
- [x] Real CascadeIDE exe dogfood (`path=` / toml on cdp-debug)
- [x] Nav Anchor in GUI lands via same `cdp_land` wire — latch `%LocalAppData%/cdp-mcp/land-LATEST.json` + CIDE `CdpLandProjector` (GoToPosition + reload clean tab)
- [x] Human GUI focus → agent cockpit sit (`focus-LATEST` internal feed → `alert.sit.locus`; agent looks at `cdp_cockpit`, not a parallel peek API)
- [ ] Melody/settings load when shell starts (do not strip)
- [x] Auto-reload open tabs on Instant Save without land — `disk-LATEST.json` (agent flush ↔ human Save); shared dirty glass
- [x] Desk→CIDE presentation topology (instant) — `presentation-LATEST.json` + live reparse; not agent `cdp_settings`; respect repo `workspace.toml`
