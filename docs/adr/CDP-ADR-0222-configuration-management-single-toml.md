# CDP-ADR-0222: Configuration management — один TOML, роли читают свои секции, без env-hell

**Status:** Accepted (slice A 2026-09-12; strict validator + legacy removal pending)
**Date:** 2026-09-10
**project-id:** `cdp-mcp` · related: CDP-ADR-0198 (durable sidecar), CDP-ADR-0203 (deploy gap), CDP-ADR-0209 (gatekeeper/slots), CDP-ADR-0221 (virtualization), CDP-ADR-0207 (canon layers)
**Tags:** #cdp #adr #config #toml #habitat #configuration-management

---

## Context

Практика 2026-09-10 вскрыла configuration hell: **три копии** одного `cdp-mcp.toml` (в `D:\cdp-mcp`, `D:\cdp-service`, `D:\cdp-gatekeeper`), каждая читается по-своему, часть ключей мёртвая, часть дублируется:

| Файл | Читатель | Что реально берёт |
|------|----------|-------------------|
| `D:\cdp-mcp\cdp-mcp.toml` | **мост** `CdpBridgeConfigLoader` | bind, port→вышка, token, install_dir/auto_start |
| `D:\cdp-service\cdp-mcp.toml` | **слот** `CdpSettings.Load` | enabled, bind, port, token (`port` — **мёртвый**: слот берёт `PickFreePort`) |
| `D:\cdp-gatekeeper\cdp-mcp.toml` | **вышка** `CdpGatekeeperHost` | **не читает toml вообще** — файл мёртвый |

Проблемы:

1. **Один файл, две схемы парсинга.** Слот читает `CdpTomlService` (нет `install_dir/auto_start`), мост — `BridgeTomlService` (есть). Разные классы, разное понимание одного TOML.
2. **Мёртвые ключи.** `port` у слота игнорируется (`PickFreePort`); конфиг вышки не нужен вышке.
3. **Дрейф копий.** Правка одной копии не синхронизирует остальные.
4. **`install_dir` закомментирован** → авто-спавн слота отключён → ручной `Start-CdpService.ps1` после каждого рестарта.
5. **env-оверрайды** (`CDP_SERVICE_URL`, `CDP_SERVICE_INSTALL_DIR`, …) дублируют TOML — **второй слой истины**, который TOML как раз должен был исключить.

## Decision

**Единый `cdp-mcp.toml` — один источник конфигурации для всех ролей (мост, слот, вышка).** Роль определяется **флагом запуска** (`--gatekeeper` / `--service` / stdio), а не отдельным файлом. Каждая роль читает **свою секцию** из одного TOML. **Env-оверрайды конфигурации — запрещены** (TOML и есть источник; env — только для не-конфигурационных операционных переменных, см. Anti-patterns).

### Структура единого TOML

```toml
version = 1

[tower]                    # вышка (CdpGatekeeper --gatekeeper)
  listen_port = 8771        # порт, который держит вышка навсегда
  registry = "auto"         # witdb слотов (по умолчанию %LocalAppData%/cdp-mcp/slots.witdb)
  target_cache_ms = 2000
  probe_timeout_ms = 3000

[slots]                    # слоты (CdpService --service)
  install_dir = "D:/cdp-service"    # где живут бинарники слота (для авто-спавна)
  auto_start = true                 # мост/ensurer поднимает слот при cold boot
  port_range = [8772, 8871]         # диапазон свободных портов для слотов
  heartbeat_seconds = 5
  registry = "auto"                 # та же witdb-коллекция слотов

[bridge]                   # мост (CdpMcpBridge, stdio)
  base_url = "http://127.0.0.1:8771"   # на какую вышку ходит
  token_path = ""                      # по умолчанию %LocalAppData%/cdp-mcp/service-token
  auto_start_slot = true               # ensurer поднимает слот при connection refused

[service]                  # legacy-алиас → [tower]+[slots]+[bridge] (обратная совместимость, см. ниже)
  enabled = true
  bind = "127.0.0.1"
  port = 8771
  token_path = ""

# Прочие секции (memory, dev, languages, canon, ...) — без изменений, общие для всех ролей.
```

### Принципы

1. **Один файл, один путь.** Все процессы получают один `--config <path>` (или `CDP_MCP_CONFIG` как единственный допустимый env — но не как оверрайд, а как «какой файл читать»).
2. **Одна схема парсинга.** Общий `CdpConfigLoader` (единый DTO-класс), а не два (`CdpTomlService` + `BridgeTomlService`). Роль выбирает, какие секции читать, но формат один.
3. **Каждый ключ читается ровно одной ролью.** Нет мёртвых ключей: `[tower]` читает только вышка, `[slots]` — только слот/ensurer, `[bridge]` — только мост.
4. **Дефолты в коде**, TOML только переопределяет (как сейчас).
5. **Валидатор**: неизвестный ключ → warn (не тихо игнорировать); отсутствующая секция для роли → дефолт роли.
6. **`[service]` — legacy-алиас** с маппингом `enabled/bind/port/token_path` → `[tower]+[slots]+[bridge]`, для старых конфигов до миграции. После миграции удаляется.

### Авто-спавн слота (fix #4)

`[slots] auto_start = true` + `install_dir` → ensurer моста поднимает слот при cold boot (connection refused на вышке, слотов нет). Это штатный механизм ADR-0198/0203, просто **включённый** в конфиге, а не закомментированный.

### Вышка не читает TOML-настройки, кроме `[tower]`

`CdpGatekeeperHost` остаётся stateless: читает только `[tower]` (порт, registry). Не знает про слоты/деплой/конфиги (ADR-0209 §1).

## Consequences

- **+** Один источник — нет дрейфа копий.
- **+** Одна схема парсинга — нет рассинхрона понимания.
- **+** Нет мёртвых конфигов (вышка больше не требует свой файл).
- **+** Авто-спавн слота включён — рестарт OC не требует ручного `Start-CdpService.ps1`.
- **−** Миграция существующих трёх копий в один файл (одноразово).
- **−** Требует единый валидатор + тесты на «ключ читается ролью».

## Anti-patterns

- **Env-оверрайды конфигурации** (`CDP_SERVICE_URL`, `CDP_SERVICE_INSTALL_DIR`, `CDP_SERVICE_AUTO_START`, …) — запрещены. TOML — источник; env-слой — это тот env-hell, который TOML должен был исключить. Единственный допустимый env — `CDP_MCP_CONFIG` (путь к файлу) и не-конфигурационные операционные (например, `CDP_RG` — путь к ripgrep-инструменту).
- Держать **две схемы парсинга** одного файла (`CdpTomlService` + `BridgeTomlService`) — рассинхрон неизбежен.
- **Отдельный файл на роль** — дрейф копий, мёртвые конфиги.
- **Молчаливо игнорировать неизвестные ключи** — скрытый конфиг-hell вместо явной ошибки/warn.
- **`install_dir`/`auto_start` закомментированы** — авто-спавн слота выключен, рестарт = ручной подъём.

## Criteria

Одна фраза: *«один cdp-mcp.toml — источник для всех ролей; роль = флаг запуска; каждая секция читается ровно одной ролью; env не оверрайдит конфиг; авто-спавн слота работает из [slots]»* — и указывают этот ADR + `CdpConfigLoader` + `[tower]/[slots]/[bridge]` в конфиге.

## Open Questions

1. **Миграция существующих трёх копий** — единый файл кладём в `D:\cdp-service\cdp-mcp.toml` (владелец слота) и остальные два делаем ссылками/одноразово удаляем? Или в общий `%LocalAppData%`?
2. **`CDP_MCP_CONFIG`** как единственный env (путь) — оставить, или тоже убрать в пользу жёсткого `--config`?
3. **Валидатор** — warn по умолчанию, или есть флаг `strict` для CI (fail на неизвестный ключ)?