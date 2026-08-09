# Voice Letter — Face silence SoftFL

**Орган:** citizen · Completions · `BuildWholeCatalog` / `TurnViaMeAi` stream fallback  
**Ship:** 0.5.707 + live dogfood 2026-08-09

---

Света орала в Radio «НАПИШИ БУКВУ». Не вредность Face — я сам её заткнул.

0.5.706: весь Meta+bare (~95 схем) в каждый `AsAIAgent` turn. Kimi + жирная history → `timeout` / `empty_text` → Glass `MarkStatus error` **без письма**. Tip=Кир — это tip≠Face, не «все умерли»; Autoi remount tips ещё и заливали ленту.

Чинит: `cdp_call` = весь catalog как reachability (не thrash) · agent пустой → stream speech · трассы не клею в letter. Dogfood: inject «А» → `А`. Mentions SoftFL — не трогал.
