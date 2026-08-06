# Voice Letter #182 — normalize changed= · need path= · need analytics_id=

**organ:** citizen · AppendKbNormalizeBits · TipKbArgException path/analytics_id  
**lived:** 2026-08-06 · SoftFL invent after thin fail / chars-only normalize

После VL#181 densest residual dig (live wire):
- `normalize_sections` → только `chars=63210` (hot-context content leak) без `changed=`
- `finding_check` / `analytics_upsert` → pulse `failed` (ArgumentException) → SoftFL invent recovery

Теперь: `changed`/`unchanged` + chars на normalize preview · hot chars только с active_scope|loaded_sections · tip `need path=` / `need analytics_id=`. SoftFL invent REJECT. Не Hold.

**live dogfood** dual hard `0.5.675` `build_utc=2026-08-06T19:05:54Z`:
- `normalize_sections` → `changed chars=63210` ack applied
- `finding_check` → `need path=`
- `analytics_upsert` → `need analytics_id=`
