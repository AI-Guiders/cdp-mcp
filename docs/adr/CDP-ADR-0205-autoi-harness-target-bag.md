# CDP-ADR-0205: AutoIgnition harness seat + provider target bag

**Status:** Accepted · Pending implementation (OpenCode leaf)  
**Date:** 2026-08-24  
**Extends:** ADR-0198 (sidecar) · ADR-0200 (tenant multiplex) · Install-Cdp `HostAdapter`  
**Supersedes (shape):** flat `IgniteArm.OpencodeSession` / env-gated `IdeAutoiFireProvider.Resolve()` as seat selectors

## Problem

AutoIgnition already needs more than one wake seat (Cursor Composer CDT, OpenCode HTTP/CLI, later Glass, maybe other IDE hosts). Early dogfood added:

- `harness=` on arm (good — explicit seat),
- then **flat** arm fields (`OpencodeSession`, and Cursor still uses top-level `Chat`/`Port`),
- plus a **parallel** registry (`IAutoiFireProvider` + `IdeAutoiFireProvider.Resolve()`) that picks Cursor vs OpenCode by **env / `IsActive()`**, not by the arm.

That combination does not scale: each new harness adds typed fields and `if harness=` branches on the shared arm schema, while the registry still ignores the arm's seat.

## Decision

### 1. `harness=` is the only seat selector on arm

Canonical values (align with Install-Cdp `HostAdapter`):

`cursor | claude | vscode | windsurf | antigravity | opencode | none`

- Arm stamps **one** harness at arm-time.
- Fire resolves provider **only** from `arm.Harness` (not from env flags).
- Env / sidecar files remain **plumbing defaults inside the provider** (URL, auth, directory), never the seat chooser.

`none` = arm may latch / schedule, but fire is a no-op (or explicit refuse) — useful for continuity without a Composer gun.

### 2. Provider owns a target bag — arm schema stays stable

Arm wire (conceptual):

```text
harness=<id>
target={ ... provider-private keys ... }
```

Persist / scene expose `harness` + opaque `target` object. **No new top-level typed fields** when a harness is added.

| Harness | Target bag (v1) | Notes |
|---------|-----------------|-------|
| `opencode` | `session` (required), `directory?`, `auth?` / password hint | HTTP `prompt_async`; env fills gaps only if bag omits |
| `cursor` | `chat?` / `conversation_id?`, `port?` | CDT inject; defaults from tenant composer latch / 9222 |
| `claude` / `vscode` / `windsurf` / `antigravity` | provider-defined | Install HostAdapter exists; **fire may be `not_implemented` until a provider ships** |
| `none` | `{}` | no delivery |
| Glass (future) | Glass-private bag | slots into the same registry — **zero arm-schema change** |

**Citizen Completions is not an AutoI harness.** Live FM turns are gated by `[citizen] enabled=` (kill switch). Habitat `prefer_citizen` fallthrough stays orthogonal and currently off. Do not reintroduce `harness=citizen` as a seat unless a later ADR explicitly brings Completions back as a first-class gun.

### 3. Registry keyed by harness

Evolve existing `IAutoiFireProvider`:

```text
IdeAutoiFireProvider.Resolve(harness) → IAutoiFireProvider
provider.FireAsync(message, target, wait, ct)
```

- Unknown harness → refuse with clear error (no silent Cursor fallthrough).
- Known but not fire-capable yet → `not_implemented` (install path may still work via HostAdapter).
- Provider validates its own bag (`session` required for OpenCode, etc.).

### 4. Parse ergonomics (flat → bag)

Agents may keep writing flat arm args:

```text
cdp_ignite op=arm harness=opencode session=ses_… directory=…
cdp_ignite op=arm harness=cursor chat=… port=9222
```

Parse **folds** known flat keys into `target{}` for that harness. Persist stores the bag. Compat: legacy `OpencodeSession` / top-level `Chat`/`Port` migrate into the bag on read.

## Non-goals (this ADR)

- Implementing Claude/VS Code/… fire providers (registry stubs OK).
- Replacing Install-Cdp HostAdapter (same id space; different job: MCP merge vs wake delivery).
- Multi-root OpenCode workspace (separate dogfood on fork branch; see Consequences).

## Consequences

+ Arm schema stops growing with harnesses; Glass slots in as another provider.  
+ Seat selection and delivery share one axis (`harness` → registry).  
+ Env demoted to plumbing — matches lived OpenCode sidecar dogfood.  
− One-time migrate: drop flat `OpencodeSession` after bag + compat readers.  
− Install-capable ≠ fire-capable until each provider lands.

## Implementation sketch (OC leaf)

1. ADR accepted (this file).
2. `IgniteArm`: `Harness` + `Target` (JSON object / dict); deprecate flat OC/Cursor delivery fields.
3. `IdeAutoiFireProvider.Resolve(string harness)`; delete env-based seat pick.
4. OpenCode + Cursor providers consume `target`; Fire.cs loses `if harness=` special cases beyond resolve.
5. Parse folds `session=`/`directory=`/`chat=`/`port=` into bag; scene pulse shows `harness` + bag keys (redact auth).
6. Tests: arm persist round-trip; OC requires session; unknown harness refuse; `none` no-op.

## Related

- Live seat dogfood: `harness=opencode session=…` already wakes OC Desktop.
- Multi-root OC: fork `D:\Experiments\opencode` branch `feat/ai-guiders-multi-root-dogfood` + `examples/ai-guiders.code-workspace` (orthogonal leaf).
- Citizen off: `[citizen] enabled=false` (2026-08-24) — Completions not a wake seat.
