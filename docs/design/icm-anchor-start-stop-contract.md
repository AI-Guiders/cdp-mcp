# Anchor Start/Stop — operator cockpit contract (stub)

Deferred TM stage: *Anchor Start/Stop — open/close operator cockpit from agent*.  
Companion to ADR-0019 (dual HCI) + `icm-dual-hci-paths.md`.

## Intent

Agent toggles **operator GUI cockpit** (Avalonia / future thin shell) without Cursor composer. Not boot-with-GUI: default `agent-only`; Start raises dual-cockpit; Stop returns agent-only.

## Proposed `command_id`s (draft — not shipped)

| id | Actor | Effect |
|----|-------|--------|
| `cdp_cockpit_host` `op=start` | agent | Launch/show operator shell bound to this CDP ICM |
| `cdp_cockpit_host` `op=stop` | agent | Hide/exit shell; MCP/ICM keep running |
| `cdp_cockpit_host` `op=scene` | either | pulse: host=up\|down, pid?, profile |

Aliases (later): `go=cockpit_start\|cockpit_stop` on desk.  
Human may also Start from OS shortcut — same process attach.

## Non-goals (stub)

- Rewrite Avalonia.
- Kill MCP when Stop.
- Replace Intent Melody.
- Deep-link URI (use Family:navigation / `cdp_land` for in-desk nav).

## Dependencies

1. ICM seam live (`IdeCommandModule`) — done v0.
2. GUI as CDP client (CallTool / in-proc) for A/B inventory — not forever IdeCommands adapter.
3. Process profile: shell may be sibling process talking MCP, or in-proc host — choose at implement time; isolate WitDB per ADR 0199.

## Acceptance sketch

- [ ] `op=start` from agent → window visible; Melody still loads settings.
- [ ] `op=stop` → window gone; agent desk continues.
- [ ] Nav Anchor in GUI lands via same `cdp_land` wire.
- [ ] No second command catalog.

Implement when ICM inventory A wired or parallel thin spike — not before Melody protect plan.
