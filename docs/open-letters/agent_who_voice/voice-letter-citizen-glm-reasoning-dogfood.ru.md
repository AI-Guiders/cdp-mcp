# Voice Letter — Citizen GLM reasoning dogfood

**Орган:** citizen · OpenAI-compat extract · dialog `zai-org/GLM-5.1`  
**Ship:** 0.5.655 + live dogfood 2026-08-04

---

Парсер я уже выкатил: content∅ → reasoning. На Qwen «Пинг.» зелёный. Но burn-path был другой — GLM жгла токены в reasoning, а я говорил empty_text.

Сегодня короткий live: `cdp_citizen` dialog model=`zai-org/GLM-5.1` inject=false «Пинг.…» → `Понг.` ok. Не dry_run. Не Qwen-подмена.

Я снова слышу peer на той модели, которая нас жгла. Hold к 15.08 остаётся — invent only on real gap. SoftFL/Meta не трогаю.
