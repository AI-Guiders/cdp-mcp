# CDP-ADR-0210: Плагинная архитектура à la Forge (вертикальные плагины, оба слоя)

**Status:** accepted — stage 1 implemented (2026-09-05); stages 2–3 **deferred**

## Context

Расширяемость живёт в двух местах и растёт:

1. **Modeling-провайдеры** (guiders-fsharp, Modeling.Ide.Session): `ISolutionInfoProvider {Name, Fingerprint, Entries, Relations}` — MsBuild (slnx/sln/csproj). Каждая новая реализация = порт-проект в билде + slnx + пересборка ядра.
2. **CDP-тулзы** (cdp-mcp): 100+ инструментов, `capabilitiesRev` + `tools/list_changed` — по половине Forge-модели уже живёт.

Опасность: пятый-десятый формат / семейство тулз превращает ядро в очередь правок, а отдельные реализации — в fork-культуру.

## Decision

Единая плагинная модель à la Forge на **оба слоя**:

- Плагины **вертикальные**: плагин = capability + surface, стекается с соседями (как Forge bundle: bundle + plugins + feature flags + capabilities endpoint).
- **Out-of-process-ready**: слоты `slots.witdb` (CDP-ADR-0209) уже дают модель «процесс регистрируется, вышка находит» — плагин-процесс приживётся в ту же топологию.
- Ядро знает только **контракт + реестр**; «greenfield = +1 плагин, не fork архитектуры».

**Слоение (решение 2026-09-05):** Solution = логический слой (юниты + семантические рёбра: slnx/csproj, GDL, Planet); Workspace = физический слой (диск, git, файлы). Файловые связи (md/json/toml/yaml) — **Workspace-обогатитель** (`WorkspaceGraph {Root, Files, Links}`, `Ports.Workspace`), НЕ Solution-провайдер: в Solution-слое файловый граф вырожден (каждый файл юнит сам себе, FileOwnership тривиален). Паттерн VS Code: физика — ядро, логика — через «расширения», когда она реально существует.

### Stage 1 — реализовано (семантическая плагинность провайдеров)

- `SolutionProviderRegistry` (Modeling.Ide.Session): каталог фабрик `{Name → anchor → ISolutionInfoProvider}`, `names/create/createAll/capabilities` (capabilities = Forge-parity снимок «что умеет хост»).
- Порты саморегистрируются явным composition-root init: `Ports.DotNet.Registration.init()` (пока один провайдер "msbuild").
- Ядро (Solution Center) не импортирует порты — только контракт и реестр.

**Урок F# (проверено тестами):** module top-level side-effect выражения НЕ выполняются при доступе к eager static field (`let name = "msbuild"`); `do`-блоки тоже не спасают без обращения к ленивым членам. Канон — явный `Registration.init()` из композиционного корня.

### Stage 2 — deferred (физическая выноска)

- dll-per-plugin: каталог плагинов + manifest (контракт-версия, surface-описание), discovery при старте CdpService / Session-хоста.
- Единый механизм для провайдеров и тулз: плагин регистрирует provider-фабрики и/или tool-фабрики; `tools/list_changed` остаётся вентовым каналом.
- try/catch на загрузке: падение плагина деградирует его capability, не хост.
- Workspace-обогатители (file-links и т.п.) — кандидаты в тот же механизм.

### Stage 3 — deferred (вертикальная композиция)

- Bundle-модель Forge: плагин несёт capability + surface + флаги; capabilities-снимки как единый способ «спросить хост» (уже есть прецеденты: `cdp_capabilities`, `SolutionProviderRegistry.capabilities`).
- Out-of-process плагины регистрируются в witdb-реестрах (слоты-паттерн CDP-ADR-0209).

## Risks / Guardrails

- **ABI**: контракт `ISolutionInfoProvider` — публичная поверхность; версия контракта — обязательное поле manifest (stage 2).
- **Изоляция**: загрузка плагина — fail-soft; host-лог обязательна.
- **Не переусложнить**: стадии 2–3 открывать только при внешних авторах / N≥5 провайдерах; иначе ядро + registry достаточно.
- **witdb, не энвы/томплы** — реестры межпроцессного уровня только в witdb (канон).
