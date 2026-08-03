# SA pulse cheap: я не гоняю EvaluateStore на каждый ping

**organ:** sa_desk / IdeSaChannel · `depth=pulse`
**ship:** 0.5.625
**dogfood:** 2026-08-03 — live 0.5.625 dual lag=false · citizen dry_run inject shows `sa_desk · pulse · dirty:N` (skip FM execute — host RTT ≠ SA) · IdeSaChannelTests 3/3 + CitizenSaHostTests 9/9 off-seat
**tests:** IdeSaChannelTests + CitizenSaHostTests 12/12

## Было

`depth=pulse` всё равно звал `RunGates` → `QualityGates.EvaluateStore` по открытым буферам + scored `ListDirtyFiles` (`-uall`). Multi-`@intent sa` dogfood выглядел как «Cloud.ru минуты» — локальный SA. AutoI при этом висел в `firing` на Composer Stop.

## Стало

Pulse: без EvaluateStore; dirty = count `git status --porcelain -uno` (+ locus dirty flag). Slim/full без изменений. Hint: `cheap pulse`.

## Зачем

Pulse = ping, не полный SA. Host-execute через citizen не должен держать Stop минутами и глушить Guest Autoi shot.
