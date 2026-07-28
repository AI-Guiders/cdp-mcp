# Melody → CDP inventory (read-only)

Source: `cascade-ide/IntentMelody/intent-catalog.toml` (~90 `command_id`s).  
Rule (ADR-0019): do **not** mutate Melody/settings here — map only. Human keep Melody UX; execute converges on CDP `command_id` / ICM.

## Buckets

### A — Near 1:1 (same or close id already on CDP)

| Melody `command_id` | CDP target |
|---------------------|------------|
| `git_*` (branch/commit/diff/fetch/log/preflight*/pull/push/status/submodule) | `git_*` domain (ICM) |
| `debug_launch` / `debug_attach` / `debug_continue` / `debug_stop` / `debug_step_*` | `cdp_debug` ops |
| `build` / `build_structured` | `cdp_build` |
| `run_tests` / `run_affected_tests` | `cdp_test` |
| `search_workspace_text` | `cdp_search` / `find_in_files` |
| `get_current_file_diagnostics` | `cdp_buffer` `op=diagnostics` / get_diagnostics |
| `open_file` / `load_solution` / `open_*_dialog` (path half) | `cdp_open` + `cdp_files` / `cdp_buffer` |
| `apply_edit` | `cdp_buffer` edit / `cdp_edit_plan` |

### B — Agent desk already covers (GUI projects later)

| Human intent (Melody today) | CDP already |
|-----------------------------|-------------|
| Nav / reveal / focus editor | `cdp_land`, `cdp_editor_scene`, `cdp_buffer` |
| Terminal panel | `cdp_shell_*` |
| Settings / options | `cdp_settings` |
| Cockpit command line | `cdp_cockpit` `cmd=` |

### C — CIDE-chrome / Intercom (stay local longer)

Chat spine, Intercom attach, MFD/PFD toggles, presentation layout, web AI portal, hybrid index page, product spine — **operator UI**. Not first ICM migrate. Keep handlers until GUI shell is CDP client for desk only.

### D — Missing on CDP (gap)

| Melody | Note |
|--------|------|
| `run_code_cleanup` | no direct `cdp_*` yet |
| Doc correspondence / feature docs / templates | FM/buffer open paths possible; no dedicated Meta |
| Solution create dialogs | `cdp_project_*` / `cdp_sln_*` partial |

## Migration order (when GUI → CDP client)

1. **A** — wire Melody `command_id` → ICM CallTool (same id or thin alias table in shell, not forever IdeCommands SSOT).
2. **B** — GUI chrome calls existing desk Metas (`cdp_land` first for nav Anchor).
3. **C** — defer with Avalonia shell.
4. **D** — add CDP Meta only when agent also needs it.

**Invariant:** leave `intent-catalog.toml` / `CascadeIdeSettings` untouched until alias table is designed.
