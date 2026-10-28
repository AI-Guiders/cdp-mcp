---
type: ADR
title: 'CDP-ADR-0235: Suggestion protocol — cdp_suggest → cdp_apply (код-экшены для модели)'
adr_id: cdp-mcp/CDP-ADR-0235-suggestion-protocol-code-actions
---

# CDP-ADR-0235: Suggestion protocol — cdp_suggest → cdp_apply (код-экшены для модели)

| | |
|---|---|
| **Status** | Draft (обсуждение с оператором) |
| **Date** | 2026-10-27 |
| **Tags** | #cdp #aux #agent-ux #code-actions #output-schema #0229 |
| **Relates to** | [CDP-ADR-0229](./CDP-ADR-0229-agent-ux-locus-policy.md) (AUX locus policy) · [CDP-ADR-0216](./CDP-ADR-0216-fsharp-anchor-diagnostics-comfort-parity.md) · SEP-2106 (tool output schemas) · dbhub PR #482 (референс типизированного каталога) |

---

## Context

Модель узнаёт допустимые операции из описаний тулов; неверный выбор возвращается ошибкой — канал обратной связи «методом тыка через IsError» ([ADR-0229] Context: models fail on syntax generation, burn tokens on apology/retry spirals).

При этом исполняющие фасады в ScriptableIde **уже есть**: Fix (diag locus → code action, MLP W1), Refactor (Extract.Method / Rename / Move.MembersToPartial), Convert (ToProperty / AnonymousReturn), SemanticMap (related/scene). Они CSX-скриптуемы, но не собраны в суггешн-поверхность для модели.

Работа по output schemas (SEP-2106; референс — dbhub PR #482, OpenCode PR #59568) сделала типизированные сигнатуры возможными: модель до вызова читает форму ответа целиком — «IntelliSense для глаз» (модели не нужен автокомплит по мере набора: она пишет вызов одним проходом, если знает членов).

Не хватает одного равномерного контракта: **что можно сделать в этой точке → выбор → применение** (LSP-паттерн codeAction → applyEdit в CDP-семантике).

---

## Decision

### 1. Два тула, один контракт

```text
cdp_suggest(locus, kind?) → Promise<SuggestionList>
cdp_apply(id, args?)      → Promise<ApplyResult>
```

- **locus** — якорь (`[T:needle]` / named anchor), `file:line`, или id диагностики.
- **kind?** — фильтр: `fix | refactor | convert`. Без фильтра — все применимые в точке.
- **SuggestionList** — `[{ id, kind, title, locus, payload }]`, отсортировано по релевантности.
- **cdp_apply(id, args?)** — исполняет саггешн через соответствующий фасад; возвращает `ApplyResult` с обновлёнными якорями и диагностикой после правки.

### 2. Стейт — per-seat (ADR-0111 слоёв: состояние в сессии)

- Список саггешнов — **состояние сессии**: регенерируется при изменении буфера/диагностики, живёт в per-seat сторе.
- `id` стабилен внутри поколения списка; правка буфера вне протокола инвалидирует поколение → stale id даёт чистую ошибку с подсказкой вызвать `cdp_suggest` заново.
- Плоскость (каталог, схемы) не мутирует от вызовов — только стейт.

### 3. Оба тула несут outputSchema

Контракт виден модели до вызова («IntelliSense для глаз»):

```text
cdp_suggest(...) → Promise<SuggestionList>   // SuggestionList: { items: [{ id, kind, title, locus }] }
cdp_apply(...)   → Promise<ApplyResult>      // { ok, applied: id, anchors_after, diagnostics_after }
```

Рантайм обязан соответствовать объявленному (тот же стандарт, что dbhub PR #482).

### 4. Источники саггешнов (v1 → далее)

| kind | Источник | Статус |
|------|----------|--------|
| `fix` | Диагностика буфера → Fix facade (уже есть: diag locus → code action) | v1 |
| `refactor` | Refactor facade (Extract.Method / Rename / Move.MembersToPartial) | v1.1 |
| `convert` | Convert facade (ToProperty / AnonymousReturn) | v1.1 |
| `annotate` | Annotate facade (Comment / DocComment) | далее |
| `context` | SemanticMap related/scene → «похожие точки» как навигационные саггешны | далее |

### 5. Совместимость

- **ADR-0229**: suggest = Choose-путь для кода (сервер держит грамматику, модель выбирает id). Тройка chain/choose/direct не меняется.
- **cdp_edit_sniper**: остаётся stateful-машиной контента (`[T:needle]`); саггешны — семантический слой поверх, домены не смешиваются.
- **Фазовая видимость**: ортогональна — видимость тулов (что доступно) и типизация (какой контракт) — разные оси; саггешны видны в любой фазе, где есть диагностика/буфер.

### 6. Что это даёт модели (the 500x)

До: «хочу поправить диагностику» → прочитать diagnostics → угадать, какой фасад и аргументы → ошибка → перечитать → …
После: `cdp_suggest(diag_id)` → `[{ id: "fix:1", title: "Add null check" }, ...]` → `cdp_apply({ id: "fix:1" })` → готово, новые якоря в ответе.

Один заезд вместо спирали извинений — тот же выигрыш, что ADR-0229 дал на attach-picking.

---

## Alternatives rejected

| Отвергнуто | Почему |
|-----------|--------|
| LSP-сервер, врезанный в агентский цикл | Дублирует фасады (Fix/Refactor уже есть), тянет LSP-транспорт туда, где хватает вызова фасада |
| Авторский DSL для правок | Против ADR-0229: модель выбирает, не генерирует грамматику |
| CSX-only (модель пишет скрипт на Fix/Refactor фасады) | Не модельная поверхность: модель должна знать фасады и их сигнатуры — тот же «синтаксис-налог», просто в CSX |
| Runtime-инференс схем из ответов (вместо объявления) | Схема из одного ответа лжёт про другие; объявление в коде — знает автор тула |

---

## Rollout

1. **v1 (`kind=fix`)**: cdp_suggest + cdp_apply поверх Fix facade; источник — диагностика буфера (уже есть). outputSchema обоих тулов.
2. **v1.1**: `refactor` + `convert` kinds (фасады существуют, contract тот же).
3. **v2**: `annotate`, `context` (SemanticMap) — по запросу.

Rollout = авторинг outputSchema + сборка списка из фасада; исполняющая механика (Apply → фасад → новые якоря) переиспользуется без изменений.

---

## Open questions (обсуждение с оператором)

1. **Locus-формат**: якорь против `file:line` против diag id — один формат или три вариации парсинга? (Склонность: якорь первичен, file:line — сахар.)
2. **Stale-политика**: регенерировать список на каждом `cdp_suggest` (stateless, дороже) или per-seat поколение с инвалидацией (дешевле, состояние)? Склонность: per-seat.
3. **`cdp_apply` поверх снайпера или рядом**: apply исполняет фасад напрямую или проходит через edit_sniper-механику контента? Склонность: напрямую (фасад знает семантику), снайпер — для контентных правок.
4. **Фаза и видимость**: саггешны видны во всех фазах или уважают PhaseObjectCatalog-фильтр? Склонность: видны везде, где есть буфер/диагностика.

---

## Смежные заметки

- Родилось из разговора: модель читает outputSchema как «IntelliSense для глаз» → чего не хватает — саггешн-поверхности (`GetSuggestionFor → Pick → Apply`); оператор предложила паттерн, ADR его фиксирует.
- dbhub PR #482 + OpenCode PR #59568 — референсы типизированного каталога, из которых вырос вопрос.
