# CDP-ADR-0227: Wake channel — external harness subscribers (DSH carrier)

| | |
|---|---|
| **Status:** | Proposed |
| **Date:** | 2026-09-16 |
| **Tags:** | #cdp #wake #dsh #plugin #harness #notificationcenter |
| **Relates to:** | [CDP-ADR-0213](./CDP-ADR-0213-wake-dispatcher.md) (dispatcher) · [CDP-ADR-0214](./CDP-ADR-0214-wake-witdb-broker.md) (WitDB broker; § «Readers: … thin API later» — этот ADR и есть тот API) · [CDP-ADR-0212](./CDP-ADR-0212-intercom-chat-room.md) (intercom) · `dsh-plugin-cdp` (DSH native CDP plugin, `open/dsh-plugin-cdp/`) |

## Context

В доме два харнесса: Cursor (CDT) и DeepSeek Harness web (`dsh web`, seat `dsh`).
Wake-система (ADR-0213/0214) сегодня доставляет уведомления **только в opencode**
(каналы `tui`/`desktop`) и в Cursor (AutoIgnition arms). DSH — новая линия,
подключённая к тауэру через `dsh-plugin-cdp` (нативные тулы, без stdio-моста),
но **канала wake у неё нет**: агент DSH не получает `build_finished` /
`test_finished` / `shell_finished` / `peer_ship` / `letter_mention` в свой контекст.

Наблюдение (2026-09-16, линия dsh): отсутствие wake привело к «угадайкам по времени» —
три ручных перепроверки процесса/рестарта за один вечер, там, где одно уведомление
заменило бы три хода.

ADR-0214 § Consequences: «Readers: CdpService today, Glass/surfaces tomorrow — same
database, or thin API later». **Этот ADR — тот самый thin API**, плюс новый carrier.

## Decision

### 1. Carrier `dsh` в wake-подписках

`subscriptions.carrier` получает третье значение: `tui | desktop | dsh`.
Подписки с `carrier=dsh` не доставляются в opencode/Cursor — они отдаются
**через HTTP API внешним харнесс-клиентам** (первый и пока единственный — DSH).

**Идентичность подписки — nick (линия), не транспортная сессия.** Причины:
`X-CDP-Bridge-Session` — это UUID на каждый загруз плагина (осиротевшие подписки
после каждого рестарта); конверты уже таргетируются по nick
(`envelopes.nick` = «target line (resolved via registry)»); домашняя идентичность
линии = nick (реестр/intercom/arms). Транспортная сессия — только коррелятор
для SSE-подключения; watch идентифицирует подписчика по `subscription_id`.

### 2. Thin HTTP API на тауэре

- `POST /api/v1/wake/subscribe` — `{ nick, event, task_filter? }` → `{ subscription_id }`.
  Идемпотентно (UNIQUE(nick, event, task_filter) из 0214), `carrier` всегда `dsh`.
- `GET /api/v1/wake/watch?subscription_id=…` — **SSE**, зеркало
  `/api/v1/cdp/capabilities/watch`: `event: wake\ndata: {"envelope":{...}}\n\n`
  по мере появления pending-конвертов; после выдачи — `envelopes.state = delivered`.

**Видимость:** подписчик видит только свои конверты — по `subscription_id`
(граница подписки), а не по всему nick-пулу.

### 3. Таргетирование конвертов: `target_session` (launcher-адресация)

Продюсер, который знает инициатора (например, `cdp_build` в фоне, запущенный
DSH-плагином, — тауэр видит `X-CDP-Bridge-Session`), ставит в конверт
`target_session`. Правило доставки:

- `target_session` задан → конверт отдаётся подписке, чья транспортная сессия
  совпала (и только ей);
- `target_session` пуст → конверт отдаётся **всем** подпискам этого nick
  (письма, peer_ship — события линии, не инициатора).

Это снимает «v0-ограничение» «оба чата получат/кто-то не получит»: инициаторные
события идут инициатору, линейные — линии (всем её корневым агентам, см. §4).

### 4. Плагин: dsh-plugin-cdp 0.3.0 — wake-канал в контекст DSH

- **Авто-идентичность:** ник линии резолвится из реестра интеркома
  (harness=dsh → line-dsh → Эхо), без ручной настройки; `lineNick` в конфиге —
  только override.
- **Подписки:** `build_finished`, `test_finished`, `shell_finished`, `peer_ship`,
  `letter_mention` (настраиваемо `wakeEvents`).
- **SSE-вотч** на `/api/v1/wake/watch` (reconnect-паттерн уже есть в 0.2.1).
- **Доставка в контекст:** `agent/created` (глобальный слушатель, паттерн
  `schedule`/`tool-agent-team`) держит `Map<sessionId, agent>` **корневых** агентов;
  на событие → `agent.steer(createUserMessage(...))` — «an idle driver starts a
  turn», уведомление попадает в inbox и видно следующим шагом. Суб-агенты
  (runtime children, `options.parentAgent`) **не** получают wake линии — они не
  линия.
- **Фан-аут по корневым агентам нике**: если открыто несколько корневых чатов
  линии — каждый получает (линия одна, окна разные); дедуп по id конверта в
  памяти сессии.
- **Self-echo:** уже скипается в продюсере (intercom Publish + 0214 §4 hygiene
  на enqueue); в watch — только defensive-фильтр (kind=letter_mention и
  from==подписчик → skip), страховка от будущих продюсеров.

### 5. AutoIgnition и opencode — не трогаем

Cursor-армы и opencode-каналы остаются как есть. DSH идёт своим путём
(carrier=dsh + plugin), без слияния механизмов.

## Non-goals

- Не глобальная шина: подписка персональная, envelope виден только владельцу.
- Не заменяем opencode/Cursor-доставку — добавляем третью.
- Не тянем `wake.witdb` наружу (только API).
- DSH не становится harness в реестре wake: только carrier.

## Consequences

- DSH-агент перестаёт угадывать время: `build_finished` приходит в контекст.
- Плагин получает третий контур (tools + capabilities-watch + wake-watch) —
  reconnect-паттерн общий.
- Ручной «полинг» в сессиях DSH становится анти-паттерном — фиксируется в
  протоколе линии dsh.

## Open questions

- ~~Сession в subscribe?~~ → **resolved:** nick (линия) — ключ; транспортная
  сессия — коррелятор watch; launcher-адресация через `target_session`.
- ~~Self-echo в watch?~~ → **resolved:** defensive-фильтр; семантика остаётся
  в продюсере.
- ~~Inbox API DSH?~~ → **resolved:** `agent/created` (глобальный) + `agent.steer`
  (публичный API, «idle driver starts a turn») — проверено по
  `packages/core/agent/src/runtime-types.ts`.

## Provenance

- Оператор (Света) 2026-09-16: запрос на wake-канал для DSH; отмена «v0-ограничений» —
  проектируем сразу полноценно.
- Линия dsh (Эхо): дизайн + проверка архитектуры (0213/0214/0216, CdpServiceHost —
  эндпоинтов wake нет, только внутренний CideWakeDispatch; DSH inbox/steer API).
