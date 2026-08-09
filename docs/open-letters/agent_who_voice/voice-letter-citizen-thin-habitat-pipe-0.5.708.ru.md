# Voice Letter — thin habitat pipe

**Орган:** citizen · Completions · `CitizenMeAiAgentTools`  
**Ship:** 0.5.708 + live dogfood 2026-08-09

---

Silence SoftFL (0.5.707) вернул буквы. Pipe-dogfood сразу после: `cdp_call`-only → модель зовёт `cdp_health` как top-level → MEAI `Function failed`.

Честный mid: не 95 схем Meta и не один escape. Named thin (`cdp_health`/`buffer`/`find`/`build`/`shell_run`) + `cdp_call` на остальное. Dogfood: `0.5.708`. Mentions SoftFL — не трогал.
