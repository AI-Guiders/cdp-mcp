# CDP-ADR-0208: Language Resolver Center — CDP first host

- **Status:** Accepted · In progress (P2 F# parity slice landed)
- **Implementation:** LRC dispatch in CDP — **7 bare verbs green** on `guiders-fsharp` slnx via `Adapters.Fcs` (`CdpLrcDispatchTests`); rename `apply` via `SessionOrchestrator.applyPatch`; ω project resolve via `FileOwnership`; **CompilerServices materialize** via `FederationSessionRuntime.TryEnsureCompilerServices` before LRC dispatch (ADR-0062 slice); active-pattern blocker; workspace symbol identity via `FSharpSymbol.IsEffectivelySameAs`; ide-session federation pulse partial
- **Date:** 2026-09-02
- **Tags:** #cdp #lrc #fsharp #fcs #roslyn #gdl #ide #mcp
- **Normative:** [GUIDERS-ADR-0061](https://github.com/AI-Guiders/guiders-dotnet-platform/blob/main/docs/adr/GUIDERS-ADR-0061-language-resolver-center.md) — federation LRC SSOT
- **Related:** ADR-0201 (cdp_peek) · ADR-0204 (outline) · ADR-0200 (tenant) · [GUIDERS-ADR-0025](https://github.com/AI-Guiders/guiders-dotnet-platform/blob/main/docs/adr/GUIDERS-ADR-0025-language-intelligence-boundary.md) · [GUIDERS-ADR-0059](https://github.com/AI-Guiders/guiders-platform/blob/main/docs/adr/GUIDERS-ADR-0059-gdl-hyperlane.md) · IdeLanguageTools.Dispatch

## Context

CDP routes bare IDE verbs through `IdeLanguageTools.Dispatch` with a flat language switch. Problems today:

- `.fs` masquerades as `csharp` — Roslyn cannot typecheck F#.
- No unified envelopes — TS and others use ad hoc shapes.
- `*.gdl` has no resolver slot while `Platform.Modeling.Gdl.*` already parses in federation.

**Federation decision:** LRC lives in **`Platform.Modeling.Language` + `Platform.Execution.Language`** — not under `AIGuiders.Cdp.*`. See [GUIDERS-ADR-0061](https://github.com/AI-Guiders/guiders-dotnet-platform/blob/main/docs/adr/GUIDERS-ADR-0061-language-resolver-center.md).

**CDP role:** **first host** — MCP wire, DocumentStore, tenant multiplex, `cdp_capabilities` — the Agent Env planet that dogfoods federation IDE roads ([Constitution](https://github.com/AI-Guiders/guiders-dotnet-platform/blob/main/docs/GUIDERS-FEDERATION-CONSTITUTION.md) § Agent Env).

## Decision

### 1. CDP does not own LRC kernel

```text
bare IDE verb ──► IdeLanguageTools (CDP)
                      │
                      ▼
              Platform.Execution.Language   ← federation (guiders-platform)
                      │
        ┌─────────────┼─────────────┬─────────────┐
        ▼             ▼             ▼             ▼
   Adapters.Roslyn  Adapters.Fcs  Adapters.Ts  Adapters.Gdl
   (platform)        (fsharp)       (platform)   (fsharp → Modeling.Gdl.*)
        │             │             │             │
        └────────► Platform.Modeling.Language ◄───┘
                   (fsharp — kernel envelopes)
                      │
                      ▼
              System.Text.Json → MCP (CDP boundary only)
```

**Rejected:** `AIGuiders.Cdp.Modeling.Language` as SSOT — superseded by federation packages.

### 2. CDP wiring

| CDP concern | Package / module |
|-------------|------------------|
| Resolver + backends | `ProjectReference` → `Platform.Execution.Language` (+ adapters) |
| MCP serialize | `CdpMcp` — maps `Modeling.Language` types to tool JSON |
| Document text | `IdeLanguageTools` DocumentStore (unchanged ownership) |
| Language catalog | `cdp_capabilities.languages` — mirrors federation `LanguageId` |
| Sniper / buffer | stays CDP + [LanguageIntelligence](https://github.com/AI-Guiders/guiders-dotnet-platform/blob/main/docs/adr/GUIDERS-ADR-0025-language-intelligence-boundary.md); consumes LRC for goto/symbols |

Sibling wire (local dev):

```text
CdpMcp → Platform.Execution.Language (guiders-platform)
       → Platform.Modeling.Language (guiders-fsharp)
```

Use `eng/Guiders.Modeling.props` pattern or explicit sibling refs — same as Cockpit Phase D.

### 3. GDL in CDP habitat

GDL uses federation **`Adapters.Gdl`** — thin map to `Platform.Modeling.Gdl.Parse.*`. CDP does **not** duplicate `GdlFragment` or quarry parsers.

| Capability | v1 target |
|------------|-----------|
| `get_diagnostics` on `*.deck.gdl` / `*.catalog.gdl` | F5 federation / P5 CDP |
| `get_document_symbols` | outline from quarry IR |
| `go_to_definition` | import graph |
| Sniper `[G:;…]` | P8 — aligns with LanguageIntelligence |

Authoring Guild owns GDL; planets reference only ([GUIDERS-ADR-0048](https://github.com/AI-Guiders/guiders-platform/blob/main/docs/adr/GUIDERS-ADR-0048-authoring-quarry-family.md)).

### 4. Capabilities (CDP surface)

```text
cdp_capabilities.languages += fsharp, gdl
cdp_capabilities.domains += fsharp, gdl
catalog = f(phase, object [, language])
```

## Migration phases (CDP)

Aligns with [GUIDERS-ADR-0061](https://github.com/AI-Guiders/guiders-dotnet-platform/blob/main/docs/adr/GUIDERS-ADR-0061-language-resolver-center.md) federation phases:

```text
P0  ADR-0208 rewrite + stop .fs → Roslyn routing (language id fix)
P1  Sibling refs to Platform.Modeling.Language + Platform.Execution.Language scaffolds
P2  IdeLanguageTools.Dispatch → LanguageResolverCenter (Fcs path first) — **7 verbs shipped** (find/completions/symbol/rename + prior trio)
P3  Roslyn path → federation Adapters.Roslyn (replace in-proc shape drift)
P4  Ts worker → Adapters.TypeScript
P5  Gdl path → Adapters.Gdl (deck + catalog pilots)
P6  Analyzers in diagnostic stream (FSharp.Analyzers + Gdl)
P7–P10  GDL goto, sniper axis, fix=, rename/peel — habitat features on stable LRC surface
```

**Order:** federation **F0–F1** (kernel + resolver scaffold) may land in parallel with CDP **P0–P1**. CDP **P2+** requires federation packages to exist.

**Pilots:** `guiders-fsharp` (FCS + GDL adapters); `guiders-platform` (Roslyn smoke workspace).

## Consequences

- CDP stops being accidental SSOT for multi-language IDE envelopes.
- Other planets embed LRC without forking CDP — reference `Platform.Execution.Language` directly.
- MCP JSON remains CDP-specific; kernel types are federation F#.

## Non-goals (CDP v1)

- Owning `LanguageDiagnostic` / `LanguageSymbol` in `AIGuiders.Cdp.*`.
- Planet DSL backends (`.dashspec`) in CDP LRC.
- Blocking CDP P0 on full platform wave B/C completion.

## References

- [GUIDERS-ADR-0061](https://github.com/AI-Guiders/guiders-dotnet-platform/blob/main/docs/adr/GUIDERS-ADR-0061-language-resolver-center.md) — federation normative
- [GUIDERS-ADR-0025](https://github.com/AI-Guiders/guiders-dotnet-platform/blob/main/docs/adr/GUIDERS-ADR-0025-language-intelligence-boundary.md)
- [GUIDERS-FSHARP-ADR-0002](https://github.com/AI-Guiders/guiders-fsharp/blob/main/docs/adr/GUIDERS-FSHARP-ADR-0002-model-guild-fsharp-ownership.md)
- CDP `IdeLanguageTools.Dispatch.cs` · `RoslynMcp`
