---
type: ADR
title: 'CDP-ADR-0236: KB AutoShip secret gate — gitleaks (content-based) instead of the filename heuristic'
adr_id: cdp-mcp/CDP-ADR-0236-kb-autoship-secret-gate-gitleaks
---

# CDP-ADR-0236: KB AutoShip secret gate — gitleaks (content-based) instead of the filename heuristic

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-11-01 |
| **Tags** | #cdp #autoship #review #security-gate |
| **Relates to** | [CDP-ADR-0224](./CDP-ADR-0224-kb-autoship-filewatcher.md) (KB AutoShip FileWatcher) |

---

## Context

`KbAutoShipEngine` blocked shipping while any dirty path scored `Risk="secret"` from `IdeReviewChannel.ScoreRisk` — a **filename** heuristic (`secret`/`credential`/`.env`/`.pfx`/`.pem` in the path). Two failures, both observed live (2026-11-01):

1. **False negatives by design**: a real credential pasted into an innocently-named file (`note-*.md`, `*.patch.yml`) ships unnoticed. The gate did not gate content at all.
2. **Deadlock on removal**: a *deleted* secret-named path still scored `secret`, so the one commit that would clear the flag — the deletion — could never ship. Observed: the declassified `agent-notes/secret-mode-prefix-ru.md` stub blocked every cycle (`blocked: 1 secret-risk path(s)`).

## Decision

The ship gate is **gitleaks over the working tree** (`detect --no-git --redact --exit-code 1`), replacing the `IdeReviewChannel` name-lane as the AutoShip blocker:

- **Findings (exit 1) → alert + abort**; scanner error (other non-zero) → alert + abort; clean → ship.
- **Missing scanner → fail-open + alert** ("gitleaks not found — secret gate skipped"): an absent tool must not brick the shipper of a personal KB. `secret_scanner = off` silences deliberately.
- Resolution: `[kb.autoship] secret_scanner` = `"auto"` (default: PATH, then the winget portable location — the host process outlives PATH refreshes) | explicit path | `"off"`. Optional `secret_scanner_config` — default: repo-local `.gitleaks.toml` when present (gitleaks config with `useDefault = true` + KB allowlists).
- `IdeReviewChannel.ScoreRisk` keeps its name-based lanes **as review-UX heuristic only** — it no longer participates in ship gating.
- Deletions are naturally fine: content scan of the working tree has no dead names in it.

Baseline evidence (first run over agent-notes, 126 MB / 5.4 s): **1 real finding** — a live `CONTEXT7_API_KEY` in a habitat profile patch; the old gate had never seen it. Allowlist policy is the repo owner's call per finding, never blanket.

## Implementation

- `KbAutoShipSecretScanner.cs` — exec + resolution + exit-code contract; `ExecOverride` for tests.
- `KbAutoShipEngine.TryShip` — scanner replaces the name-lane count; missing-scanner alert, blocked → AlertFail.
- `KbAutoShipOptions` / `CdpSettings` — `secret_scanner`, `secret_scanner_config` (snake_case TOML, `[kb.autoship]`).
- `CdpMcp.Tests/KbAutoShipSecretGateTests.cs` — findings block; clean ships; missing scanner fails open with alert; `off` disables.

## Consequences

- The gate now catches real secrets regardless of file names, and never deadlocks on deletions.
- False positives (KB speaks *about* tokens in prose) are handled per-path in the repo's `.gitleaks.toml`, versioned with the content it allows.
- One more external binary in the toolchain (gitleaks, MIT, winget); absence degrades loudly to fail-open, not silently.
