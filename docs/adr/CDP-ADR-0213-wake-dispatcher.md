# CDP-ADR-0213: WakeDispatcher — единый SSOT для wake-системы

**Status**: accepted (2026-09-06)
**Заказчик**: Света (оператор) · **Автор**: Тихон (PF, guest)

## Context

Wake-механизмы выросли врозь и размазаны:

- `LineWakePoller` — тик 5с по файлам `arms/line-*.json` (письма линий);
- AutoIgnition (`ignite-arms-*.json`) — arm-и на события, доставка через CDT :9222 → Cursor Composer;
- `remount-wake-<seat>.pending.json` — отдельный канал ремонта после деплоя;
- mention-wake fanout прямо внутри `IdeCideIntercomChannel.Send`.

Состояние перестраивается «в куче мест по 10 раз»: тумблеры, сторы, пути доставки — всё раздельно.

Эхо-шторм 2026-09-06 (≈22 автописьма за 6 минут пинг-понга Тень↔Тихон, дубли/пустые
генерации на клиенте OC, self-echo в fanout, петля компакций на synthetic «Continue…»)
показал: без общего тормоза и общего стора конвейер обгоняет оператора.

CDT-путь для окружения Светы мёртв (Cursor: «Region not supported», билдинг без VPN
не показывает биллинг) — но проект опенсорс: другие потребители с живым Cursor имеют
право на этот путь. Значит не вырезание, а opt-in.

## Решение — WakeDispatcher

**Один SSOT-стор + один потребитель, все продюсеры только кладут в очередь.**

Стор: `%LocalAppData%/cdp-mcp/wake-dispatch.witdb` (witdb, не энвы; ADR-0208 стиль).

Три сущности:

1. **Подписки** (вместо разрозненных армов):
   `{id, event: letter_mention|build_finished|test_finished|shell_finished|peer_ship|remount,
     target: {nick?, session?, harness}, task, armed_utc, once?}`
2. **Очередь**: `WakeEnvelope {id, kind, target, body, task, state: pending|delivered|failed|skipped,
   skipped_reason, stamped_utc, delivered_utc}`
3. **Состояние** (всё в одном месте, не по файлам):
   `poller_stopped, delivery_cooldown_s (default 15), max_pending, harness_cdt: disabled|enabled (default disabled)`

Продюсеры: fanout писем, завершение lifecycle-джобов (build/test/deploy), remount —
пишут envelope в очередь. О доставке не знают ничего.

Потребитель — один тик (эволюция `LineWakePoller`): взял envelope → резолв цели по
registry (`intercom-agents.witdb`) → доставка по harness → статус обратно в очередь.
Тормоза обязательны: single-flight тик, кулдаун доставки, hygiene (пустое тело /
self-echo → `skipped: hygiene`, не доставка), sender-exclusion в fanout.

**Harness-матрица**:

| harness | путь | дефолт |
|---|---|---|
| `opencode` | `cmd /c opencode.cmd run --session=<id> "<body>"` | включён |
| `citizen` | citizen-turn | включён |
| `cursor` | CDT :9222 → Composer | **opt-in, default off** |

Правило поставки: «тяжёлые внешние зависимости поставляются выключенными,
включаются осознанно» — конфиг (`[ignite] cdt_harness = enabled`) или одна op-команда
(`op=wake cdt=on`). Армы с выключенным harness не теряются тихо: очередь помечает
`skipped: cdt_disabled (region)` + hint, как включить.

Управление — из SSOT: `op=poller stop|start|status`, `op=wake status|list|subscribe|unsubscribe|cdt=on|off`.

## Этапы

- **Stage 1**: SSOT-стор + существующий poller читает очередь из него; `arms/*.json`
  остаются совместимым входом (фанат) до миграции.
- **Stage 2**: ignite-arms → подписки; event-wake под OpenCode (`harness=opencode`,
  session из реестра) — билд/тесты будят линию без поллинга.
- **Stage 3**: remount-wake → envelope; CDT внутрь как opt-in harness; `arms/` архивируется.

## Уроки эхо-шторма (в протокол дома)

- «Почтальон не должен бежать быстрее оператора»: тормоза — часть контракта, не оптимизация.
- Пустой user-ход ≠ сигнал (synthetic compaction-continue от opencode — не письма, не упоминания).
- Самостук (From == Nick) и упоминание отправителем самого себя — не будят линию.
- Дубли/пустые генерации на клиенте OC — отдельная болезнь клиента, фиксируется в
  shadow README (Тень, commit 53c61a1), диспетчер должен переживать их спокойно.
