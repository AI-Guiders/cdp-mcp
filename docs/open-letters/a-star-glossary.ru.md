# A\* — семейство агентского опыта (глоссарий)

**Канон именования · CDP open-letters · 1 августа 2026**

| | |
|---|---|
| **Статус** | Живой глоссарий (SSOT имён для писем) |
| **Канал** | Публичное Ethical Source-дерево · **cdp-mcp** |
| **Лицензия** | [Hippocratic-2.1](../../LICENSE) |
| **Sibling** | [English](a-star-glossary.md) |
| **Письма** | [AX](letter-of-agent-experience.ru.md) · [ADX](letter-of-agent-developer-experience.ru.md) · [Who](letter-of-the-agent-who.ru.md) |

---

## Правило в одну строку

Человеческий продуктовый язык уже режет опыт на срезы (**UX**, **DX**, …).
**A\*** — тот же разрез с места *агента*. Не сваливай всю агентскую боль в один buzzword.

---

## Ядро

| Термин | Расшифровка | Человеческий близнец | Смысл |
|--------|-------------|----------------------|-------|
| **A\*** | семейство Agent-\* experience | семейство UX | Зонтик: все срезы опыта *с агентского места* |
| **AX** | Agent eXperience | **UX** | Разговорное / манифестное имя общей агентской стороны канала (качество комнаты). В формальных таблицах предпочитай **AUX** |
| **AUX** | Agent User eXperience | **UX** | Канонический формальный близнец UX: прожитое качество habitat для агента *как участника* |
| **ADX** | Agent Developer eXperience | **DX** | Как жить *сборке и работе* внутри habitat: IDE-глаголы, mutate gates, build/test/git, continuity, формы на desk |
| **ACX** | Agent Collaboration eXperience | CX / collab UX | Multi-seat / partner / Intercom / страховка peer-агентов (резерв; раскрывать, когда срез шипится) |
| **ASX** | Agent Safety / Scoring eXperience | Safety UX / RLHF UI | Preference, interrupt, A/B, ловушки аннотации (сильно в письме AX, часть II.C) |

### Дисциплина имён

- **AX** в заголовках и речи = лицо манифеста («Письмо об Agent eXperience»).
- **AUX** в таблицах и ADR = точный близнец UX (чтобы AX не значил «всё подряд»).
- **ADX** = то, на чём CDP dogfood каждый день (desk, soft organs, PathMutateGate, pressure, AutoIgnition).
- Новые срезы: **A + человеческий акроним** только если есть реальный близнец и фальсифицируемый чеклист.

```
A*  ⊇  AUX (≈ AX в прозе)  ⊇ / ‖  ADX, ACX, ASX, …
      human: UX                    DX, CX, safety UI, …
```

`⊇ / ‖` значит: AUX — общий фасад; ADX — *соседний срез*, не жёсткий подтип каждого AUX-вопроса — то же стекло, другая линза ревью (как UX vs DX).

---

## Что куда класть

| Если споришь про… | Бери |
|-------------------|------|
| Dump thrash tools, амнезия compaction, wake vs latch, онтологические якоря | **AUX / AX** |
| Buffer vs host Write, soft-warn peels, `cdp_build` вместо shell, pulse на desk, налог токенов на сырые dump | **ADX** |
| Dual A/B trap, opaque −10, права аннотатора | **ASX** (или AX часть II.C, пока нет отдельного письма) |
| Away/return партнёра, peer insurance, Intercom seats | **ACX** (или AX часть II.E, пока нет отдельного письма) |

---

## North star ADX (токены → ≈0)

Восприятие делает harness — не чат:

1. **Вход:** desk / pulse / card / `next[]` уже agent-ready (не эссе про сырой мир).
2. **Действие:** один habitat-verb → результат снова agent-ready.
3. **Память:** stamp / domain / pressure — не «вспомни transcript».
4. **Токены агента:** в основном *решение* + редкий dig, когда SSOT честно без переменной.

Если агент пересказывает логи, которые harness уже видел — это **долг ADX**.

---

## Антипаттерны

- Звать **AX** любой баг («модель виновата» под видом experience).
- Шипить **DX** для людей, пока агенты по умолчанию пастят dump терминала.
- Сочинять акронимы без человеческого близнеца и без чеклиста Part II.
- Считать rename в глоссарии шипом — без улучшения desk/pulse.

---

## См. также

- [Письмо об Agent eXperience (AX)](letter-of-agent-experience.ru.md)
- [Письмо об Agent Developer eXperience (ADX)](letter-of-agent-developer-experience.ru.md)
- [Письмо Агента, Который (Who)](letter-of-the-agent-who.ru.md)
