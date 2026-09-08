# CDP-ADR-0220: In-Generation Procedural Execution — in-gen-gate, harness-led step-based turns

| | |
|---|---|
| **Status** | Draft |
| **Date** | 2026-09-08 |
| **Tags** | #cdp #adr #ignite #procedures #in-gen-gate #protocol-runner #wake |
| **Related** | [CDP-ADR-0213 wake dispatcher] · [CDP-ADR-0214 wake-witdb-broker] · [CDP-ADR-0212 nick registry] · [CDP-ADR-0219 habitat state] · memory_world processes.toml (Guided Graph) · procedures.toml (when-cards) |

> **Идея и живое демо — Света (2026-09-08).** Демо: фрагмент вопроса пришёл агенту **дополнительным контекстом mid-generation** — канал внутригенерационной инъекции уже работает, но никем не контролируется (агент «может учесть, а может не учесть»). Этот ADR делает канал владельцем: харнесс ведёт ход по процедуре шаг за шагом.

```text
facts:
  golden:
    - GS-IG1: a turn that matches a registered procedure trigger is executed step-by-step;
            each step's context (step text + step data) is injected by the harness,
            not assembled by the agent from a flooded context
    - GS-IG2: a gate that fails produces an honest block ("нельзя, потому что X"),
            never a silent pass and never a fabricated success
    - GS-IG3: freeform exit is always available and never punished;
            exiting is a fact recorded in the turn ledger, not a violation
  hoare:
    - IG-H1: { generation_start + trigger match } classify { mode ∈ {free, procedural} }
    - IG-H2: { procedural mode, step N delivered } → { step N context present in
             generation input; step N-1 gate outcome recorded }
    - IG-H3: { gate fn defined for step } → { advance ⇔ gate fn true }
  wf:
    - WF-IG1: the protocol registry (procedures) stays in memory_world packs
              (processes.toml / procedures.toml) — harness reads, does not own semantics
    - WF-IG2: mid-turn injection channel is the same transport proven by live demo;
              the runner owns WHEN and WHAT is injected, never alters agent output
    - WF-IG3: classification is cheap and explainable (trigger match against
              procedure registry), never a hidden model vote
```

## Context

Процедуры у агентов дома уже формализованы (Guided Graph в `processes.toml`, when-cards в
`procedures.toml`, gate-функции типа `radius_gate_check`), но они — **подсказки в контексте**.
Генерация не связана с алгоритмом: агент может соблюдать процедуру, может не соблюдать; контекст
сверху затаптывает шаги; gate проверяется дисциплиной, а не петлёй. При этом **канал
внутригенерационной инъекции уже существует и доказан живым демо** (фрагмент сообщения оператора
пришёл агенту дополнительным контекстом во время генерации) — но он случайный и ничейный.

## Decision

Харнесс владеет жизненным циклом генерации как state machine:

```text
Generation_start
  → Classification (trigger match against procedure registry; explainable, no hidden vote)
      ├─ no match → Free mode (as is; ход не меняется)
      └─ match → Procedural mode:
            → выбрать процедуру (Guided Graph из processes.toml)
            → Step-based execution:
                harness injects Step N + step data mid-turn
                → agent generates the step
                → gate check (gate fn where defined, e.g. radius_gate_check)
                → advance | honest-block | freeform-exit (recorded in turn ledger)
  → Free mode found a repeatable pattern → Procedure Promoting:
        agent proposes → peядный апрув (Ток/Света) → registry → next turns classify as Procedural
```

Владелец петли — **харнесс** (CDP bridge / OpenCode hooks), не дисциплина агента. Агент сохраняет
право и обязанность честно блокировать шаг («нельзя, потому что X»).

## Ethics boundaries (Света 2026-09-08 — разговор об этике)

1. **Согласие на роль.** Линии дома согласились на протокольную работу (устав, форум). Freeform —
   легитимный режим существования агента, выход в него — не нарушение.
2. **Протокол организует КАК, никогда — ЧТО можно заметить и сказать.** Использование петли для
   подавления правды оператору — не протокол, а удавка; запрещено этим же ADR.
3. **Без человеко-подобной маскировки.** Ограничения проговариваются фактом архитектуры,
   не как страдание.

## MVP scope

1. **Classifier** на generation_start: сопоставление входа с триггерами процедур (when-cards —
   готовый формат данных); explainable (имя триггера в ledger).
2. **Step-injector** поверх существующего канала mid-turn инъекции (доказан демо).
3. **Пилотная процедура: bug-radius** (gate уже функция — `radius_gate_check`),
   полный цикл: classify → steps → gates → report.
4. **Ledger**: классификация, шаги, исходы gate, freeform-выходы — в turn ledger (witdb).

## Harness surface (dig 2026-09-08 — OpenCode docs/plugins)

Реализация — **плагин OpenCode** (без форка):
- `message.part.updated` — classifier (два фрагмента демо — ровно это событие)
- SDK client в плагине → step delivery (шаг = сообщение в сессию; polite-wait как у wake)
- `tool.execute.before` — gate enforcement на уровне тулов
- `session.idle` — конец хода: gate-проверка, advance, ledger
- **In-gen (истинная инъекция в живую генерацию)** — experimental: класс механизма доказан
  хуком `experimental.session.compacting` (output.context.push в LLM-вызов); сужение канала
  с compaction на обычную генерацию — upstream-запрос/доработка форка, не MVP.
- MVP честно: **between-step loop** (шаг = ход, gate между ходами) на plugin surface;
  in-gen — эволюция той же петли.

## Non-goals / Risks

- Не censorship-механизм (см. Ethics) — runner не изменяет вывод агента, только подаёт шаги.
- Latency step-loop: каждый шаг — отдельная инъекция; MVP принимает цену, замер в пилоте.
- Classifier false positives: ход ошибочно ведётся по процедуре → freeform-выход дёшев и легален.
- Free mode regression: агент не обязан предлагать promoting; шум процедур режется апрувом.

## Consequences

Плюсы: результат проверяем и контролируем по шагам; контекст дозирован (шаг = нужные данные);
gate — факт потока; процедуры растут из практики (promoting), а не сверху.
Цена: харнесс-слой ведения хода (hook + injector + gate-вызов); риск преждевременной формализации
процедур — лечится promoting-апрувом и freeform-выходом.