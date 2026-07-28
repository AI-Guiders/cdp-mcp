# Dark Cockpit — agent attention v1

**Статус:** v1 (канон поведения + DoD рантайма CDP).  
**Параллель человеку:** [cascade-ide `flat-chrome-dark-cockpit-v1`](../../../cascade-ide/docs/design/flat-chrome-dark-cockpit-v1.md) § Dark Cockpit — agent attention.  
**Связь:** CIDE ADR [0021](../../../cascade-ide/docs/adr/0021-pfd-mfd-cockpit-attention-model.md) (salience); CDP alert/SA/`next[]`/Autoi/pressure.

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

- [ ] Plateau без open ship ECL и без fail gates → не emit SA WARN/ECL plateau.
- [ ] Soft phase affinity mismatch → advisory, не WARN (пока catalog не ломается).
- [ ] `next[]` не поднимает `n-alert` при `alert.level=clear`.
- [ ] Autoi guard: no arm/fire без focus (кроме явных remount/operator wakes).
- [ ] Тест/dogfood: post-ship empty focus → clear sit.
- [ ] Зеркало в human design doc остаётся согласованным.

## Вне scope v1

- Полный Endsley SA redesign.
- Визуальный EICAS человека (уже flat-chrome).
- Автовыбор следующей задачи после plateau (отдельный product decision).

## Файлы / locus

| Артефакт | Роль |
|-----------|------|
| этот файл | SSOT agent DC для CDP |
| cascade-ide `flat-chrome-dark-cockpit-v1.md` | параллель human + краткий контракт агента |
| `IdeSituation*` / alert channel / eQRH plateau | код, который должен соответствовать DoD |
