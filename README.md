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

Recipient does **not** clone siblings or run `dotnet publish`. CI/`package-win-release.ps1` puts a win-x64 zip on [GitHub Releases](https://github.com/AI-Guiders/cdp-mcp/releases).

```powershell
Invoke-WebRequest https://github.com/AI-Guiders/cdp-mcp/releases/latest/download/Install-Cdp.ps1 -OutFile Install-Cdp.ps1
# keep scripts/cdp-mcp.toml.example next to the script, or download it from the same release/repo
.\Install-Cdp.ps1 -HostAdapter cursor   # claude | vscode | none
```

The script downloads `CdpMcp-*-win-x64.zip`, clones [kb-public](https://github.com/AI-Guiders/kb-public) (read-only), seeds an empty personal canon. Maintainer escape: `-CdpSource D:\cdp-mcp`.

GitHub Actions (`.github/workflows/release.yml`) All build siblings (including `ai-native-ui`) are public under Hippocratic-2.1 on AI-Guiders/*. Optional org secret `GH_PAT` only if clone rate-limits bite. Tag `v*` or Run workflow.

## Build / deploy

```powershell
dotnet test ..\cdp-core\Cdp.Core.Tests\Cdp.Core.Tests.csproj -c Release
.\publish-and-deploy.ps1
.\publish-and-deploy.ps1 -Mode hard
.\publish-and-deploy.ps1 -Mode hard -Target D:\cdp-mcp-debug   # experimental spare
# Sibling cdp-core / cdp-scriptable-ide present but use nuget.org packages instead:
.\publish-and-deploy.ps1 -UseNuGet
dotnet run --project tools\CdpProbe\CdpProbe.csproj -c Release
```

`-UseNuGet` → `aid-publish -UseNuGet` → MSBuild `AidUseNuGet=true` (AIGuiders.Cdp.Core + AIGuiders.Cdp.ScriptableIde). Other backends still need the open monorepo until they grow the same package fallbacks. Requires `aid-publish` ≥ **0.1.5**.

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

[Hippocratic License 2.1](LICENSE) (Ethical Source / SPDX `Hippocratic-2.1`).

### Ethical policy (cdp-mcp organ)

- **Allowed:** fork, audit, inhabit, extend the harness.
- **Not allowed:** use for violence, repression, or other applications barred by
  Human Rights Principles / Human Rights Laws in the license text.
- Short summary here does not replace [`LICENSE`](LICENSE).
- Not OSI MIT: open code with conscience bound in SPDX.


