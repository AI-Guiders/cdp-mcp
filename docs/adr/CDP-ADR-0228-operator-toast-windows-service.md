# CDP-ADR-0228: Operator toast — отдельный Windows-only сервис (потребитель NotificationCenter)

| | |
|---|---|
| **Status:** | Proposed |
| **Date:** | 2026-09-16 |
| **Tags:** | #cdp #operator #toast #notificationcenter #windows #service |
| **Relates to:** | [CDP-ADR-0213](./CDP-ADR-0213-wake-dispatcher.md) (NotificationCenter) · [CDP-ADR-0214](./CDP-ADR-0214-wake-witdb-broker.md) (broker) · [CDP-ADR-0227](./CDP-ADR-0227-wake-channel-external-subscribers.md) (thin API, dsh carrier) |

## Context

Оператор хочет видеть завершение lifecycle-событий (apply/ship, build, test) в
Windows Notification Center. Соблазны, **отклонённые**:
- хуки `OperatorToast.Show(...)` в каждом месте завершения (IdeDurableJobRunner,
  IdeLifecycleJobs, IdeSessionLifecycle) — 6+ рассыпанных вызовов;
- встроенный в тауэр toaster — тауэр **кросс-платформенный**
  (`RuntimeIdentifiers: win/linux/osx`), тосты — Windows-only забота.

Решение оператора (2026-09-16): **отдельный сервис, Windows-specified**.

В доме уже есть NotificationCenter (ADR-0213): все lifecycle-события втекают в
`CideWakeDispatch.NotifyEvent` из одной точки (`CdpIgniteArmHost.Notify`,
строка 129) — из любого процесса (in-process, durable supervisor). Thin API
(ADR-0227) уже отдаёт эти конверты внешним потребителям.

## Decision

**Тост — отдельный Windows-only процесс-потребитель того же NotificationCenter.**
Тауэр не содержит кода тостов вообще.

1. **Тауэр (минимум):**
   - виртуальный ник `оператор` в `IntercomAgentsRoster.Resolve` (kind=operator,
     harness=toast, без записи в witdb);
   - `case "toast"` в `CdpWakeDispatcher.DeliverAsync` → доставка через тот же
     in-memory hub (потребитель — как dsh-плагин).
2. **`CdpToastService`** (новый проект, `net10.0-windows`, отдельный exe):
   - подписка `POST /api/v1/wake/subscribe {nick: оператор, event: …}` на
     `peer_ship`, `build_finished`, `test_finished`;
   - SSE-вотч `/api/v1/wake/watch?nick=оператор` (reconnect, как у плагина);
   - нативный WinRT-тост (`Windows.UI.Notifications` — проекции SDK, без
     внешних пакетов), Windows-only по построению.
3. **Запуск** — отдельно от тауэра (вручную / автозапуск / планировщик — на
   усмотрение оператора; реестр сервисов Windows — кандидат, не обязателен).

## Non-goals

- Не новый центр: используется ровно тот же NotificationCenter (0213/0214).
- Не тост в тауэре: тауэр кросс-платформенный и не знает про Notification Center Windows.
- Не тост для каждого письма/mention — только lifecycle по умолчанию.
- Не тост в dsh-плагине: плагин уведомляет агента, тост — человека.

## Consequences

- apply/ship/build/test → тост оператору; в lifecycle-коде ноль изменений.
- Новое событие доходит до оператора автоматически, если идёт через NotifyEvent.
- Отключение/события — аргументы `CdpToastService` (или конфиг), не код.

## Provenance

- Оператор (Света) 2026-09-16: «возможно сделать свой NotificationCenter —
  а то там уже и так дофига разных подписок, но по-моему этот диспетчер там
  уже есть» → диспетчер есть (0213/0214); «Надо отдельным сервисом делать)
  Windows Specified)» → отдельный Windows-only сервис-потребитель.
