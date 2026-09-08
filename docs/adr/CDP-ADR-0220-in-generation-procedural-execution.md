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

Харнесс владеет жизненным циклом генерации как **конечным автоматом** (Света 2026-09-08:
«Procedural — ты оказываешься внутри конечного автомата»; Thinking… → либо Free, либо FSM
с точной последовательностью: куда, когда, при каких условиях поворот; эскейп во Free):

```text
Generation_start
  → Classification — РЕШЕНИЕ агента: к какому классу задач отнести ход
    (не trigger match: у КЛАССА задач есть FSM, у конкретной задачи — нет;
     конкретика задачи — данные внутри состояний, не последовательность)
      ├─ класс не выбран (новизна/свободный ход) → Free mode (as is; ход не меняется)
      └─ класс выбран (e.g. session-open, bug-radius, publish-package) → Procedural mode:
            агент исполняет FSM КЛАССА:
            states = шаги класса (ожидаемый инструмент + данные класса;
                      конкретика задачи — параметры внутри состояний)
            transitions = gate-условия (advance | honest-block | override w/ rationale)
            rails = запрещённые переходы (tool.execute.before: redirect/block)
            mis-classification замечена → re-classify или escape → Free (легально, в ledger)
            terminal: DONE (gates ok) | BLOCKED | FREE-EXIT
  → Free mode found a repeatable pattern → Procedure Promoting:
        агент предлагает КЛАСС задач с FSM → peядный апрув (Ток/Света) → registry
```

FSM-формализм делает протокол класса **исполняемой спецификацией** (состояния+переходы+рельсы),
валидируемой тестами — как FSM-тесты FrozenTreeModel. Classification — агентская ответственность
(объяснима: класс + почему; ошибка класса → re-classify/escape, не катастрофа). Владелец петли —
**харнесс** (CDP bridge / OpenCode hooks), не дисциплина агента. Агент сохраняет право и обязанность
честно блокировать шаг («нельзя, потому что X»).

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
- `tool.execute.before` — **главная рельса (rails-first, Света 2026-09-08)**:
  перехват КАЖДОГО вызова инструмента в procedural mode; mismatch с процедурным
  шагом → **redirect** (подмена аргументов на процедурный вызов — плагин умеет
  менять output.args; агент получает результат правильного вызова с пометкой
  `redirected: rail(...)`) или **block** (throw с рельсой — агент видит её как
  результат инструмента в том же ходу, мгновенно). Съезд — явный override с
  rationale, фиксируется в ledger. Напоминание ПОСЛЕ хода не работает: решение
  об инструменте принимается в момент вызова — рельса стоит в точке вызова.
- `message.part.updated` — classifier (два фрагмента демо — ровно это событие)
- SDK client в плагине → подача данных шага между ходами (вспомогательный канал)
- `session.idle` — конец хода: gate-проверка, advance, ledger
- **In-gen (истинная инъекция в живую генерацию)** — experimental: класс механизма доказан
  хуком `experimental.session.compacting` (output.context.push в LLM-вызов); сужение канала
  с compaction на обычную генерацию — upstream-запрос/доработка форка, не MVP.
- MVP: **rails-first на tool.execute.before** (redirect/block по процедурному шагу) +
  between-step data delivery через SDK. In-gen — эволюция той же петли.

## Non-goals / Risks

- Не censorship-механизм (см. Ethics) — runner не изменяет вывод агента, только подаёт шаги.
- Latency step-loop: каждый шаг — отдельная инъекция; MVP принимает цену, замер в пилоте.
- Classifier false positives: ход ошибочно ведётся по процедуре → freeform-выход дёшев и легален.
- Free mode regression: агент не обязан предлагать promoting; шум процедур режется апрувом.

## Prior art (dig 2026-09-08 — не изобретение, сборка зрелых паттернов)

- **Класс A, токен-рельсы (истинный in-gen)**: Guidance/Outlines/SGLang — FSM/грамматика
  маскирует сэмплинг; OpenAI Structured Outputs — constrained decoding в проде.
  Ограничение: нужен доступ к сэмплеру — недостижимо на закрытых API (наш случай);
  эволюция при своём инференсе.
- **Класс B, харнесс-рельсы на границах вызовов — наш класс**: Claude Code hooks
  (PreToolUse block/redirect — аналог tool.execute.before; UserPromptSubmit — инъекция
  при старте хода); **system-reminders** — harness-led инъекция в ход, работающая в
  проде Anthropic ежедневно; NeMo Guardrails — rails-философия индустриально;
  LangGraph — FSM-оркестрация между LLM-вызовами (nodes/edges/conditional routing).
- **Класс C, подсказки без enforcement** (.cursorrules, procedures-in-system-prompt) —
  доказанно ненадёжен (наш стартовый пункт; напоминание после хода не действует в
  момент решения).

Позиция: ADR-0220 = класс B (rails-first FSM) поверх стокового OpenCode plugin surface;
класс A — эволюция при своём инференсе/форке.

## Consequences

Плюсы: результат проверяем и контролируем по шагам; контекст дозирован (шаг = нужные данные);
gate — факт потока; процедуры растут из практики (promoting), а не сверху.
Цена: харнесс-слой ведения хода (hook + injector + gate-вызов); риск преждевременной формализации
процедур — лечится promoting-апрувом и freeform-выходом.