# CDP-ADR-0207: Writing canon layers — route stack, guiders-style, personal operator

**Status:** accepted (direction); implementation phased  
**Date:** 2026-08-26  
**Tags:** #cdp #adr #habitat #canon #guiders-style #route  
**project-id:** `cdp-mcp` · consumers: CDP habitat · Cursor seat · all code repos with `.cdp/`

**Related:**

- [GUIDERS-VISION-0002](https://github.com/AI-Guiders/guiders-platform/blob/main/docs/vision/GUIDERS-VISION-0002-writing-canon-layers.md) (compact — survive compaction)
- CDP-ADR-0206 (HIS — revision integrity; orthogonal to writing canon)
- CDP-ADR-0018/0022 (pressure / precompact)
- KB [011](https://github.com/AI-Guiders/kb/blob/main/knowledge/adr/011-aiguiders-org-collaborative-kb-repo-v1.md) (org group KB) · [012](https://github.com/AI-Guiders/kb/blob/main/knowledge/adr/012-multi-canon-workspace-resolution-v1.md) (primary/secondary canon)
- KB [019](https://github.com/AI-Guiders/kb/blob/main/knowledge/adr/019-writing-canon-layers-and-guiders-style-v1.md) (**mirror stub** — points here)

**Trigger:** Forge Control Center Hub regressed to `ForgeHtml` string tables despite FORGE-ADRs; root cause = no **operational writing SSOT** in agent hot path. Operator: add **personal prefs** layer; normative ADR belongs in **CDP**, not KB.

---

## Context

### ADR sprawl ≠ «how we write today»

Product repos accumulate decision ADRs (Forge, DashSpec, …). Agents should not read chains per edit — context dies, legacy patterns win.

**KB worlds** (`software-dotnet-csharp`, playbooks) = depth + epistemics. **Not** a per-repo operational surface.

### This is CDP habitat routing

Layer discovery, merge order, token budgets, `canon_stack` in session route, optional pre-edit hints — **CDP MCP / CdpService**, not KB router alone.

KB ADR [019](https://github.com/AI-Guiders/kb/blob/main/knowledge/adr/019-writing-canon-layers-and-guiders-style-v1.md) remains a **one-page mirror** for KB index continuity.

---

## Decision

### 1. Two merge planes (do not flatten)

| Plane | Question | Layers |
|-------|----------|--------|
| **code** | How is **this repo** written? | global style → org lang → project → (scope leaf) |
| **operator** | How does **this operator** want the agent to work? | personal prefs (primary canon only) |

**Code plane:** project canon wins over org/lang on UI patterns, file layout, gates.  
**Operator plane:** personal prefs win on habitat (CDP vs shell), dialogue, commit/push grants, inbox rules — **do not** override project §code.

### 2. Layer stack (code plane)

```text
L0  KB META / worlds          cross-project style (stable)
L1  guiders-style             org: core/ + {lang}/ (csharp, python, ps, …)
L2  project canon             {repo}/.cdp/canon.md
L3  scope canon (optional)    umbrella monorepo leaf only
──  product ADR               decision log — not daily route
```

**guiders-style** (L1): recommended repo `AI-Guiders/guiders-style`, pin `org_style = "guiders-style@v1"` in `.cdp/project.toml`. Lang folder selected by `lang = "csharp"` etc.

### 3. Personal operator layer (operator plane)

**SSOT:** primary (personal) canon — **not** in product git repo.

| Path (convention) | Content |
|-------------------|---------|
| `knowledge/personal/operator-writing-prefs.md` | Thin operational slice |
| or `knowledge/personal/operator-guides/writing-surface-v1.md` | Same role |

Examples (not in org/project canon):

- Habitat: CDP-first, shell escape hatch rules
- Dialogue: language, tone, «ты»
- Git: logical commits + push after ship (operator grant)
- Cursor: inbox vs WORK, session-close
- **Not** Forge Razor rules — those stay in `agent-forge/.cdp/canon.md`

**Route:** CDP loads from **primary canon path** only ([KB-012](https://github.com/AI-Guiders/kb/blob/main/knowledge/adr/012-multi-canon-workspace-resolution-v1.md) P1). No copy into `knowledge/organization/`.

**Budget:** ~400–600 tokens — stub in hot optional, full body via `read_knowledge_file` on demand.

### 4. Project canon (L2) — well-known paths

```toml
# .cdp/project.toml
lang = "csharp"
org_style = "guiders-style@v1"
canon = "canon.md"
```

| File | Role |
|------|------|
| `.cdp/canon.md` | SSOT operational slice for repo |
| `PROJECT-CANON.md` (optional) | Pointer stub |

Sections: stack now · do/don’t · golden files · gates · `rev` · `supersedes_adr_routine: [...]`

### 5. ADR vs canon

| | ADR | Canon |
|---|-----|-------|
| Role | Why, alternatives, history | How **now** |
| Audience | architecture review | agent before first edit |
| Size | unlimited | hard cap ~150 lines/layer |

Paradigm shift → update canon + ADR one-liner «ops → canon §X».

### 6. CDP `canon_stack`

**Defaults:** embedded `Resources/cdp-project.defaults.toml` in `Cdp.ScriptableIde` (merge like AgentNotes MCP). Disk `{scm_root}/.cdp/project.toml` overlays.

**Tool:** `cdp_canon_stack` after `cdp_open` — JSON `canon_stack.operator[]` + `canon_stack.code[]`.

Separate from `read_hot_context` L0 ([KB-008](https://github.com/AI-Guiders/kb/blob/main/knowledge/adr/008-workspace-scope-map-hot-mcp-and-public-cut.md) — do not merge org hot with project canon).

```json
{
  "canon_stack": {
    "operator": [{ "layer": "personal", "path": "…", "exists": true, "budget": 500, "preview": "…" }],
    "code": [
      { "layer": "org-lang", "path": "…", "exists": false, "budget": 800 },
      { "layer": "project", "path": "…/.cdp/canon.md", "exists": false, "budget": 1500 }
    ]
  }
}
```

Resolution: `scm_root`; `lang` picks `{lang}/`; env `GUIDERS_STYLE_ROOT`, `OPERATOR_WRITING_PREFS_PATH`.

P3+: soft pre-edit warnings from project canon §anti-patterns.

---

## Non-goals

- Replacing KB worlds or group KB
- Personal prefs in product repo (leaks on push)
- Canon per plugin / bundle / settings scope
- Duplicating ADR bodies into canon

---

## Implementation phases

| Phase | Deliverable |
|-------|-------------|
| **P0** | This ADR + GUIDERS-VISION-0002 + KB-019 stub |
| **P1** | `agent-forge/.cdp/canon.md`; `guiders-style` stub; `operator-writing-prefs.md` skeleton |
| **P2** | `cdp_canon_stack` MCP tool; embedded `cdp-project.defaults.toml` + disk merge in `Cdp.ScriptableIde` |
| **P3** | Pre-edit hints; optional CI «canon rev on paradigm PR» |

---

## Governance

- **Convention / CDP routing** → this ADR (cdp-mcp).
- **Org C# style** → `guiders-style/csharp/`.
- **Forge UI rules** → `agent-forge/.cdp/canon.md`.
- **Operator habitat** → personal canon (primary only).
