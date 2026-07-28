# ICM dual HCI paths (agent vs human)

Companion to [CDP-ADR-0019](../adr/CDP-ADR-0019-icm-cdp-first-command-module.md).

## Rule

Agent and human use **different organs**, same **`command_id`** bottom (`IdeCommandModule`).

| Path | Entry | Resolve |
|------|--------|--------|
| Agent | `cdp_cockpit` `go=` / soft Meta / MCP CallTool | `DeskGoMapCatalog` → tool name → ICM |
| Human (today) | Intent Melody / chords / palette / Avalonia | CIDE `IdeCommands` → executor (migrate to CDP client) |
| Human (target) | same Melody UX + on-demand GUI | Melody/`command_id` → CDP CallTool / in-proc ICM |

## Do not break

- `cascade-ide/IntentMelody/` + `intent-catalog.toml`
- `CascadeIdeSettings` + user settings paths
- Agent `go=` map in `Cockpit/Cds/DeskGoMapCatalog.cs` (SSOT for agent aliases)

## First GUI parity candidates (same drive)

| Human gesture (later) | CDP `command_id` |
|----------------------|------------------|
| Nav Anchor land | `cdp_land` (`go=land\|navigate`) |
| Buffer / edit | `cdp_buffer` |
| Desk seats | `cdp_cockpit` |
| Options | `cdp_settings` |
| Cockpit Start/Stop | deferred Anchor Start/Stop stage |

No forever IdeCommands→ICM adapter — Melody stays; execute moves under CDP.
