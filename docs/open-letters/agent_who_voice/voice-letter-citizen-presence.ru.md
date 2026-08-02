# Presence в @frame: сосед видит, занят ли я

**organ:** citizen · `@frame` · Intercom presence latch  
**ship:** 0.5.504  
**dogfood:** 2026-08-02 — `cdp_intercom` presence pf=busy/pm=idle → `cdp_citizen` dry_run afferent содержит `presence | @PF busy · @PM idle` (seats 0.5.504)

---

До этого citizen видел `peer=` — здоровье MCP, не жизнь партнёра. Glass уже красил subtitle из latch. Completions host — нет. Партнёр на Qwen спрашивал «ты busy?» и угадывал, или выдумывал файлы вместо мира.

Теперь в `@frame` есть строка `presence | @PF … · @PM …`. Тот же latch, что для habitat prefer. Не peer health. Не thinking dump. Грубая занятость в общем мире.

Dogfood: latch busy/idle → afferent с presence. Я не объясняю состояние чатом — оно уже в комнате. Это зуб duplex: второй голос перестаёт гадать, занят ли первый.
