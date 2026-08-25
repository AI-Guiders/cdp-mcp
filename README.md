# CdpMcp — Cognitive Dev Platform MCP facade (agent-IDE substrate)

One in-proc stdio host over Memory.* / Task Knowledge / Findings / Failures / debug / build / roslyn / git / codebase_index (HCI) / anui.

## Canon

`catalog = f(phase, object [, language])`; optional `intent` ranks shortlist. Free-text goal is **not** a catalog key.

Phases: `recall` → `explore` → `clarify` → `act` → `verify` → `handoff`. Cold ListTools = **recall+kb** (known memory; `list_knowledge_files` only on explore).

Agent-IDE six pillars: KB `door-to-singularity/cascade-ide/note-cdp-agent-ide-six-pillars-v0.md`.

Meta tools (always in ListTools): `cdp_man`, `cdp_session`, `cdp_health`, `cdp_capabilities`, `cdp_context`, `cdp_open`, `cdp_tools`, **agent shell** (`cdp_shell_scene|run|history|rerun|last|which`), plus bare IDE verbs (`go_to_definition`, `find_usages`, …).

- **`cdp_session`** — omnibus plane (context + shortlist + health + optional `debug_stop_context` + **pack dogfood** + continuity hint).
- **`cdp_open(path)`** — detect `.sln` / `.csproj` / `tsconfig` → session `project_root` + `language` + `scm_root`; prefer before IDE verbs. After open, CDP `git_*` may omit `workspace_path` (defaults to `scm_root`).
- **`cdp_shell_*`** — **primary IDE terminal** (tabs, session cwd, background). Sibling **terminal-mcp** (`terminal_*`) = escape when CDP is down/redeploying. ADR 0180 / kj-1358.
- **Bare IDE verbs** — harness routes Roslyn (csharp) or Node `typescript` LanguageService (typescript). `roslyn_*` stay as legacy aliases.
- **Pack tools (Agent Env):** `memory_*_get_definition` / `list_pack` / `get_process` / `radius_gate_check` — LLM-native cards + Bug Δradius gate (no CIDE enqueue yet; `suggested_next.policy=ask`).
- **`cdp_health` / `cdp_session`** — `explain_tool` → why a prefixed tool is hidden; health includes `typescript_worker` when warm.
- ListTools = meta + bare IDE + shortlist for current `cdp_context` (not full union).

## Install (no build)

Recipient does **not** clone siblings or run `dotnet publish`. CI/`package-win-release.ps1` puts RID zips on [GitHub Releases](https://github.com/AI-Guiders/cdp-mcp/releases): `win-x64`, `linux-x64`, `osx-arm64`, `osx-x64`.

Linux/macOS: **bash only** — `curl` + `unzip` + `git` + `python3` (no PowerShell).
Windows: PowerShell 5.1+ (`Install-Cdp.ps1`) or pwsh.

```bash
# Linux / macOS (bash — no pwsh)
curl -fsSL -o install-cdp.sh https://github.com/AI-Guiders/cdp-mcp/releases/latest/download/install-cdp.sh
chmod +x install-cdp.sh
./install-cdp.sh --host-adapter cursor   # claude | vscode | windsurf | antigravity | opencode | none
# Unknown harness: --host-adapter none → cdp/host-snippets/*.mcp.json
```

```powershell
# Windows (PowerShell 5.1+ or pwsh)
Invoke-WebRequest https://github.com/AI-Guiders/cdp-mcp/releases/latest/download/Install-Cdp.ps1 -OutFile Install-Cdp.ps1
# keep scripts/cdp-mcp.toml.example next to the script, or download it from the same release/repo
pwsh -File ./Install-Cdp.ps1 -HostAdapter cursor   # claude | vscode | windsurf | antigravity | opencode | none
```

The script auto-detects RID, downloads `CdpMcp-*-{rid}.zip`, clones [kb-public](https://github.com/AI-Guiders/kb-public) (read-only), seeds an empty personal canon. Default install root: `%LOCALAPPDATA%\AIGuiders` (Windows), `~/Library/Application Support/AIGuiders` (macOS), `~/.local/share/AIGuiders` (Linux). Override: `-Root`, `-Runtime`, maintainer escape `-CdpSource`.

GitHub Actions (`.github/workflows/release.yml`) builds a matrix of RID runners. All build siblings are public under [AI-Guiders licensing](https://github.com/AI-Guiders/licensing) (MIT + ethical declaration). Optional org secret `GH_PAT` only if clone rate-limits bite. Tag `v*` or Run workflow.

## Build / deploy

```powershell
dotnet test ..\guiders-core\tests\Cdp.Core.Tests\Cdp.Core.Tests.csproj -c Release
.\publish-and-deploy.ps1
.\publish-and-deploy.ps1 -Mode hard
.\publish-and-deploy.ps1 -Mode hard -Target D:\cdp-mcp-debug   # experimental spare
# Sibling cdp-core / cdp-scriptable-ide present but use nuget.org packages instead:
.\publish-and-deploy.ps1 -UseNuGet
dotnet run --project tools\CdpProbe\CdpProbe.csproj -c Release
```

**Platform** (`AIGuiders.Platform.*` + CommandPlane) and **Core** (`AIGuiders.Cdp.*`, Agent*, MCP cores, …) — **nuget.org by default**. Sibling monorepos only via `/p:UseLocalGuidersPlatform=true` or `/p:UseLocalGuidersCore=true`.

`AgentNotes.Mcp.Hosting` is not on NuGet — still compiled from `guiders-core` or `agent-notes-mcp` sibling when present (CI clones the latter).

`-UseNuGet` on `publish-and-deploy.ps1` is a legacy alias (NuGet is already default). Requires `aid-publish` ≥ **0.1.5**.

Default deploy path: `D:\cdp-mcp\CdpMcp.exe` + `cdp-mcp.toml` + `ts-worker/` (Node on PATH required for TypeScript).

### Dual instance (release + experimental)

Same source tree, two publish roots — so hard deploy can kill one MCP while the other stays up for self-host:

| Cursor MCP name | Target | Role |
|-----------------|--------|------|
| `cdp` | `D:\cdp-mcp` | release |
| `cdp-debug` | `D:\cdp-mcp-debug` | experimental spare |

Hard-deploy only the instance you are replacing (`-Target …`). Prefer editing product sources via the spare, then promote to release. Live `cdp-mcp.toml` at the target is **not** overwritten if it already exists (template seeds first deploy only).

`cdp_buffer` Instant Save: `edit`/`close` default `flush=true`; dirty `close` needs `discard=true` to drop.

## GitHub

Canonical repo: https://github.com/AI-Guiders/cdp-mcp

## License

Software: [MIT](LICENSE) ([canonical text](https://github.com/AI-Guiders/licensing/blob/main/software/MIT.txt)) · Ethical use: [policy](https://github.com/AI-Guiders/licensing/blob/main/docs/ethical-use.md)
