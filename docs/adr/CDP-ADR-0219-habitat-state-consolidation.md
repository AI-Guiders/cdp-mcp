# CDP-ADR-0219: Habitat state consolidation — one witdb per state root

| | |
|---|---|
| **Status** | Draft |
| **Date** | 2026-09-07 |
| **Tags** | #cdp #adr #state #witdb #consolidation #correspondence |
| **Related** | [CDP-ADR-0199 tenant isolation] · [CDP-ADR-0200 per-tenant session] · [CDP-ADR-0209 slots] · [CDP-ADR-0213 wake dispatcher] · [CDP-ADR-0214 wake-witdb-broker] · [OutWit.Database 14 — fleet standard, locking] |

> **Статус facts-блока:** механика `Sat(facts)` (Correspondence `verified_by`) — ADR-0007 P3,
> **ещё не реализована**. Здесь facts — нормативная спека-обязательство: контракт фаз,
> проверяемый тестами и DoD вручную до появления машинной проверки. Не читать как
> работающий механизм.

```text
facts:
  golden:
    - GS-HS1: after migration, the store registry enumerates ALL runtime collections;
             no orphan state files (json/jsonl) remain under the state root
             as sources of truth (interop/export copies allowed, marked read-only)
    - GS-HS2: a crash between related writes (wake letter + nick registry update)
             leaves cdp-state consistent on restart (no half-applied pair)
  hoare:
    - HS-H1: { operation touches N collections in cdp-state } apply(N) { all-or-nothing visible }
    - HS-H2: { migration run for collection C } → { C reads/writes via CdpStateStore port;
             legacy file retired to interop-only }
  wf:
    - WF-HS1: runtime state SSOT is cdp-state.witdb (one per state root);
             json/jsonl files may exist only as export/interop, never as sources
    - WF-HS2: correspondence maps are NOT runtime state — they stay per-repo git files
             (.cascade/workspace.toml), versioned with code, never moved into the DB
end facts
prose:
```

## Context

Habitat runtime state размазан по зоопарку мелких сторов без реестра владения:

| Store | Формат | Владелец | Замечание |
|-------|--------|----------|-----------|
| intercom.witdb | witdb | Intercom journal | OK |
| slots.witdb | witdb | Slot registry (machine-level) | OK |
| intent-workspace.witdb | witdb | Per-tenant session | OK |
| intercom-agents.witdb | witdb | Nick registry (ADR-0212) | OK |
| desk seats | witdb | Desk | OK |
| wake-dispatch.json | json (tmp+rename) | Wake queue (ADR-0213) | **горело**: tmp-GUID bug (456057d) |
| intercom-identity/presence-LATEST.json | json | Latch | последняя запись без транзакций |
| ignite-arms-{seat}.json | json | Ignite arms | гонки restart |
| pressure-memo.jsonl / stash | jsonl/json | Pressure desk | append-only |
| remount-wake pending | json | Deploy | ephemeral |

Практическое доказательство класса ошибки: молчаливая порча записи (tmp-GUID) жила неделями, потому что у сторов нет реестра, наблюдаемости и атомарности между связанными записями.

## Decision

1. **Один `cdp-state.witdb` на state root** (per-tenant, ADR-0199/0200): коллекции
   `wake`, `arms`, `intercom-identity`, `intercom-presence`, `desks`, `pressure`,
   `store-registry`. Slots registry остаётся machine-level (межпроцессный, WitDbFileGate).
2. **Порт `CdpStateStore`** — единственный доступ к коллекциям; legacy-файлы читаются
   при первом старте (migration-on-first-run) и переводятся в interop-only.
3. **WitDB 14, коллекции в одном файле** — не несколько файлов под роутером:
   роутер поверх однофайловых БД теряет межфайловую атомарность и пересоздаёт
   проблемы движка на уровне приложения (отвергнуто; см. Alternatives).
4. **Correspondence-карты не мигрируют** — версионированная конфигурация репо,
   живёт git-файлами рядом с кодом (WF-HS2).
5. **SQL Server отклонён** для runtime-слоя: одиночная машина, один писатель,
   embedded/offline — сила хабитата. Резерв: аналитика по выгруженным evidence.

## Alternatives

- **Cross-file router над witdb-файлами**: против природы one-file-db (см. Decision 3);
  допустим только как переходный слой миграции, не целевое.
- **Локальный SQL Server**: см. Decision 5.

## Phased rollout

| Phase | Scope | Exit |
|-------|-------|------|
| P0 | Инвентарь + `store-registry` коллекция (кто чем владеет) | реестр полный |
| P1 | wake + arms (обжёгшийся контур) | GS-HS2 |
| P2 | intercom identity/presence + desks | GS-HS1 |
| P3 | pressure + вывод json-зоопарка в interop | GS-HS1 полный |

**Перед каждой фазой — перечитать**: ADR-0213/0214 (wake), ADR-0209 (slots — остаётся),
ADR-0199/0200 (isolation roots). Correspondence-гейт: не начинать фазу без dig.

## Consequences

- Positive: один SSOT, атомарность между подсистемами, наблюдаемость через реестр,
  один бэкап; класс «молчаливой порчи записи» закрывается транзакциями.
- Negative: один домен блокировок на подсистемы с разной частотой записи
  (mitigation: короткие транзакции, witdb row/page locks); миграционная работа.
- Risk: deployment/restart семантика — WitDbFileGate держит файл; проверить
  сценарий KillRunning (P1 обязательный тест).

## DI requirement (Света 2026-09-08, «нормальный DI, чтобы изолировать что угодно»)

Хабитат-компоненты — статик-классы со разбросанными override-хуками (Transport, WitDbPathOverride, temp-файлы) — **не DI**. Целевая форма:

1. **Интерфейсы окружения** (каждый — единственная точка зависимости компонента):
   - `IWakeTransport` — доставка (есть: IOpencodeWakeTransport).
   - `IStateRootProvider` — ResolveStateRoot() вместо рассыпанных путей.
   - `IAgentRoster` — Resolve/ResolveDefaultSeat/Claim (обёртка CideIntercomAgents).
   - `ICdpStateStore` — порт коллекций (есть как статик → инстанс).
2. **Компоненты — экземпляры с конструкторной инъекцией**: `WakeDispatcher(roster, transport, store, options)`, `IgniteArmHost(...)`, `IntercomAgents(...)`. Статик-фасады делегируют скомпонованному инстансу (переходный период).
3. **Composition root** — CdpServiceHost собирает реальный граф при старте (порты/пути/конфиг — один раз). Тесты собирают свой граф: temp-каталог инъекцией через IStateRootProvider (не Path.GetTempPath хардкодом), фейковый ростер, фейковый транспорт. Ноль статик-мутаций, ноль temp-файлов.
4. **Порядок конвертации** (по одному компоненту за коммит, зелёные тесты): wake dispatcher → ignite arm host → intercom agents → stores. Pilot = wake dispatcher.
5. Запрет на новые статик-мутации/override-хуки (гейт code review).

## Non-goals

- Перенос correspondence-карт/KB в базу (WF-HS2).
- SQL Server в runtime-слое.
- Миграция в один PR.

```text
end prose
```
