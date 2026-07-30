# Dark Cockpit — agent attention v1

**Статус:** v1 (канон поведения + DoD рантайма CDP).  
**Параллель человеку:** [cascade-ide `flat-chrome-dark-cockpit-v1`](../../../cascade-ide/docs/design/flat-chrome-dark-cockpit-v1.md) § Dark Cockpit — agent attention.  
**Связь:** CIDE ADR [0021](../../../cascade-ide/docs/adr/0021-pfd-mfd-cockpit-attention-model.md) (salience); seats Scan Pattern (ADR 0191 / `IdeDeskSeats`); CDP alert/SA/`next[]`/Autoi/pressure.  
**Citizen wire (draft):** [citizen-agent-wire-v0.md](./citizen-agent-wire-v0.md) — system + frame format for in-habitat agent (not Cursor-guest MCP).

## Определение (одной фразой)

**Agent Dark Cockpit** = та же политика внимания, что у человека (в норме тихо; salience только по отклонению), применённая к **агентским каналам** (`alert` / SA / `next[]` / `pulse` / eQRH / Autoi charge / pressure notify), а не к пикселям.

Тишина в норме ≠ «агент слепой». Тишина = не жечь токены и внимание на декоративный thrash, когда sit уже OK.

## Две оси (не путать с human)

| Ось | Human DC | Agent DC |
|-----|----------|----------|
| **Носитель** | лампы, center, EICAS strip | `alert.level`, SA pulse, `next[]`, Autoi, pressure |
| **Норма** | center тёмный, без цветной полосы | `alert.level=clear`; нет WARN/ECL на здоровой sit |
| **Salience** | цвет только W/C/A | шум только на **реальном** отклонении, требующем действия |
| **Пустота** | лампа off = healthy | intentional plateau / `focus=null` после ship ≠ failure |

## Agent Scan Pattern (parallel human SP)

**Не сводить** агентский маршрут внимания к одному человечьему `p→forward→m`.

У человека Scan Pattern — в основном **география экрана** (три слота, предсказуемый взгляд). У агента та же география — **якорь** (ADR 0191 / seats wire: `view.scan = p→forward→m`, replace-in-seat, не append tiles). Поверх неё — **слои внимания**, которых у пиксельного кокпита нет или они другие.

### Формула (slim desk / cockpit v1.20)

`board → sa → next → drill`

| Такт | Что читать | Зачем |
|------|------------|-------|
| **1. Geography** | `view.banner` → `view.board` (или `ascii`) | Общий якорь с человеком: P / Forward / M |
| **2. Sit** | `alert` / SA pulse; `pressure?` если armed | Clear vs deviation; continuity без ритуала в чат |
| **3. Steer** | `next[]` | Только actionable; в clear — без `n-alert` / ECL-tourism |
| **4. Drill** | один `go=` / `pane_full=` / `desk_detail=nav` | По нужде хода; не W-spray `seats_detail=full` |

Первый такт совпадает с человечьим SP; **полный маршрут шире**.

### Зоны vs seats

Три seat-label’а ≠ полный набор агентских зон:

| Слой | Примеры |
|------|---------|
| **Seat P** | plan, SA desks, pressure, ignite, find/files, CRM, problems |
| **Seat Forward** | editor / buffer / sniper |
| **Seat M** | git, shell, browser, mcp, settings, ECL/QRH, script/ps1 |
| **Meta (вне seat)** | `session`, `go` result, `instrument`, nav `loci[]`, Autoi charge |

Meta — не «четвёртый монитор», а **salience layer** (тот же Dark Cockpit / W·C·A budget). Органы садятся в seats по `DefaultPolicy`; каналы sit/steer живут рядом с board, не вместо него.

### Инвариант

- Geography wire (`p→forward→m`) **сохранять** — это shared scan geography с оператором.
- Агентский SP = geography + sit + steer + optional drill — **параллель**, не замена ADR 0191 / human SP.
- Сводить всё только к трём строкам board = читать desk без `alert`/`next`/`pressure` — форма ломается.

## Что считается нормой (clear)

Ситуация **clear**, если одновременно:

1. Toolchain / gates не в fail (или fail уже обработан и не актуален).
2. Нет активного PathMutateGate / deploy / build block, блокирующего текущий ход.
3. Нет pressure L1 без stash (или stash уже есть).
4. Plateau **intentional**: нет TM focus после `done` / leftover sweep / operator idle — и нет открытого ECL ship, который агент обязан закрыть.
5. Soft mismatches (Stage `@phase` vs session phase) не ломают tool catalog.

В clear: `alert.level=clear` (или advisory max); **не** `WARN` + `ecl` + `plateau` как default scream.

## Что считается отклонением (salience OK)

| Класс | Примеры | Уровень |
|-------|---------|---------|
| **W** | build/test fail; hard gate; remount без initialized wake когда wake owed; mutate blocked и работа стоп | warn |
| **C** | focus есть, AC/DoD не closed; phase mismatch **ломает** catalog; Autoi armed без focus (blind) | caution |
| **A** | soft `@phase` drift; leftover dry-run count>0; tip «focus a task when ready» | advisory |

## Нормы для каналов

### `alert` / SA

- Clear sit → `level=clear`, pulse без WARN/ECL.
- Intentional plateau (post-ship, no focus, no open ship ECL) → **не** `WARN · ecl · plateau`; max advisory / clear.
- Soft `@phase` affinity ≠ session phase → advisory, не WARN, пока catalog цел.

### `next[]`

- Бюджет: только **actionable** deviations + 1–2 navigation aids, если реально нужны ходу.
- Запрещено маскировать evergreen tourism (`onboard`, `goto` «на всякий») под W/C.
- `n-alert` появляется только когда `alert` реально не clear.

### Autoi

- Charge / re-arm только при реальном TM focus или явно authorized wake (remount initialized, operator arm).
- Blind plateau re-arm запрещён (DC + thrash).

### Pressure

- L1 → `op=arm` → `op=stash` quietly (AutoIgnition / TM / habitat=CDP).
- Не предлагать export/ритуал в чат, пока оператор не попросит.

### Поведение агента (контракт)

1. Не эскалировать clear sit и не «чинить» plateau WARN как будто это работа.
2. Не re-arm Autoi вслепую на пустом focus.
3. Не раздувать ответ оператору ритуалами continuity при L1 — только stash.
4. При DC violation в продукте (антипример ниже) — фиксировать как bug/debt, не как операторскую ошибку.

## Антипример (dogfood 2026-07-28)

leftover AC+DoD ship → focus null → `sa WARN · ecl · plateau` + `n-alert` при здоровом toolchain.

Это **нарушение Agent Dark Cockpit**: intentional empty трактуется как ECL.

**Целевое поведение:** `alert.level=clear` (или advisory «no focus — idle OK»), без `n-alert`.

## DoD реализации (рантайм)

- [x] Plateau без open ship ECL и без fail gates → не emit SA WARN/ECL plateau (next-task optional; ignite-park auto when idle).
- [x] Soft phase affinity mismatch → advisory, не WARN (пока catalog не ломается).
- [x] `next[]` не поднимает `n-alert` при `alert.level=clear`.
- [x] Autoi guard: no arm/fire без focus (кроме явных remount/operator wakes) — уже last_once plateau guard.
- [x] Тест/dogfood: post-ship empty focus + ignite idle → clear sit.
- [x] Зеркало в human design doc остаётся согласованным.

## Вне scope v1

- Полный Endsley SA redesign.
- Визуальный EICAS человека (уже flat-chrome).
- Автовыбор следующей задачи после plateau (отдельный product decision).
- Переписывать seats wire / отменять human `p→forward→m` geography.

## Файлы / locus

| Артефакт | Роль |
|-----------|------|
| этот файл | SSOT agent DC для CDP |
| cascade-ide `flat-chrome-dark-cockpit-v1.md` | параллель human + краткий контракт агента |
| `IdeSituation*` / alert channel / eQRH plateau | код, который должен соответствовать DoD |
