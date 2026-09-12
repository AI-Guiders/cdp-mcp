# CDP — Architecture Current State (SSOT)

> **Живой источник правды о том, что сейчас.** ADR — это история (почему и когда решили). Этот файл — срез *сейчас*: компоненты, транспорт, порты, конфиг-файлы, связи, статусы, куда движемся. Обновлять при каждом принятом/изменённом ADR и при расхождении ADR↔реальность.

| | |
|---|---|
| **Status** | Living |
| **Date** | 2026-09-10 |
| **Tags** | #cdp #ssot #architecture #current-state #registry |
| **Normative** | ADR-0198 (sidecar big-bang) · ADR-0209 (gatekeeper tower) · ADR-0219 (state consolidation) · ADR-0222 (config management) |
| **Anti-pattern** | Верить одному ADR в отрыве от этого файла. ADR описывает решение, этот файл — его текущее состояние. |

---

## 1. Как обновлять

1. Принял/изменил ADR → обнови секции 2–5 (реестр, компоненты, связи, статусы).
2. Расхождение ADR↔реальность → обнови секцию 6 «Известные разрывы» **и** правь ADR/код.
3. Секция 5 (статусы) — единственное место, где ADR живут как «сейчас», а не «было решено».
4. **Автоматизация (план)**: pre-commit hook сверяет реестр (секция 5) с `docs/adr/*.md` — новый ADR без строки в реестре = fail. Реестр — по чему проверять.

---

## 2. Компоненты (сейчас)

| Компонент | Бинарник | Роль | Транспорт | Порт | Конфиг | Состояние |
|---|---|---|---|---|---|---|
| **Мост** | `CdpMcpBridge.exe` | stdio↔HTTP для харнесса | stdio (MCP) + HTTP | — | `D:\cdp-mcp\cdp-mcp.toml` | 2 инстанса (PID 15816, 31476) |
| **Вышка (gatekeeper)** | `CdpGatekeeper.exe` | маршрутизация 8771 → активный слот | **Kestrel (direct bind)** | 8771 | нет (реестр слотов из witdb) | **Kestrel с 2026-09-12; HttpListener→HTTP.sys снят** |
| **Сервис (слот)** | `CdpService.exe` | весь CDP-функционал | Kestrel | 8772+ (PickFreePort) | `D:\cdp-service\cdp-mcp.toml` | жив (0.5.764, healthz OK) |
| **Ensurer** | в мосте | воскрешение вышки/слота при провале | HTTP probe | — | `[service] install_dir` (закомментирован!) | отключён (не автозапускается) |

**Связи (ASCII):**
```
MCP-клиент (opencode) ──stdio──▶ CdpMcpBridge ──HTTP──▶ CdpGatekeeper (:8771) ──▶ CdpService (слот :8772)
                                                              ▲
                                              slots.witdb (реестр слотов, self-register)
```

---

## 3. Транспорт и порты

| Порт | Владелец по-документу | Фактический | Примечание |
|---|---|---|---|
| 8771 | CdpGatekeeper (Kestrel, ADR-0209) | **Kestrel (gatekeeper PID)** | Kestrel direct bind с 2026-09-12; порт умирает с процессом |
| 8772..8871 | CdpService (Kestrel, PickFreePort) | 8772 — Kestrel | нормально, порт умирает вместе с процессом |
| stdio | CdpMcpBridge | 2 процесса | нормально |

**Два разных стека слушателей — спроектировано** (ADR-0209): вышка на `HttpListener`, слоты на `Kestrel`. Разные жизненные циклы: Kestrel-порт освобождается с процессом, HTTP.sys-registration — переживает hard-kill.

---

## 4. Конфиг-файлы (накопленный долг → ADR-0222)

| Файл | Читает | Проблема |
|---|---|---|
| `D:\cdp-mcp\cdp-mcp.toml` | мост | есть `[service] install_dir` — **закомментирован** (ensurer отключён) |
| `D:\cdp-service\cdp-mcp.toml` | слот | `port=8771` — **мёртвый ключ**: слот берёт порт из `PickFreePort()`, не из конфига |
| `D:\cdp-gatekeeper\` | вышка | **не читает toml вовсе** (stateless, реестр из witdb) |
| `%LocalAppData%\cdp-mcp\slots.witdb` | вышка | реестр слотов (self-register, CdpSlotRegistry) |
| `%LocalAppData%\cdp-mcp\cdp-state.witdb` | слот | runtime-state SSOT (ADR-0219) |

**Направление (ADR-0222, Draft):** один `cdp-mcp.toml`, секции по ролям (`[tower]/[slots]/[bridge]/[service]` legacy), одна схема парсинга, env-оверрайды запрещены (только `CDP_MCP_CONFIG`).

---

## 5. Реестр ADR (38)

> **Единственное место, где статус ADR актуален.** Для pre-commit hook — источник правды: файл ADR ↔ строка здесь.

| ID | ADR | Статус | Дата |
|---|---|---|---|
| 0198 | CDP sidecar big-bang — durable CdpService + thin CdpMcpBridge | Accepted | — |
| 0018 | Pressure desk — L1 pre-compact prep | Accepted | — |
| 0019 | ICM — CDP-first IdeCommandModule | Accepted | — |
| 0020 | Desk vs organ path (`cdp_cockpit`) | Accepted | — |
| 0021 | Windows-first operator glass | Accepted | — |
| 0022 | Pressure memo line — anti-compaction agent archive | Accepted | — |
| 0023 | HILD — Human-in-the-loop detector | Accepted | — |
| 0024 | Recall gate — pull → reconcile → align → ready | Accepted | — |
| 0025 | Citizen vs guest isolation | Accepted | — |
| 0026 | Citizen AI keys foundation (`ai-keys.toml`) | Accepted | — |
| 0027 | Cursor guest-host OOM tooth + OOM Wake | Accepted | — |
| 0028 | Citizen agent wire (pulse frames) | Accepted | — |
| 0029 | Teeth analysis organ | Accepted | — |
| 0030 | Citizen multi-session continuity | Proposed | — |
| 0031 | Explore corr full-a before Act | Accepted | — |
| 0032 | Durable background jobs — three layers | Accepted | — |
| 0200 | Sidecar tenant multiplex | Accepted | — |
| 0201 | cdp_peek — read-only file eyes | Accepted | — |
| 0202 | Online capabilities refresh | Accepted | — |
| 0203 | Bridge deploy-gap survival | Accepted | — |
| 0204 | cdp_peek — structural outline | Accepted | — |
| 0205 | AutoIgnition harness seat + provider target bag | Accepted | — |
| 0206 | Habitat Integrity Subsystem (HIS) | Accepted | — |
| 0207 | Writing canon layers | Accepted | — |
| 0208 | Language Resolver Center — CDP first host | Accepted | — |
| 0209 | Gatekeeper tower + self-registering service slots | Accepted | — |
| 0210 | Плагинная архитектура à la Forge | Accepted | — |
| 0211 | Deploy-plane ownership — promote never from target | Accepted | — |
| 0212 | Intercom chat room | Accepted | — |
| 0213 | WakeDispatcher — единый SSOT для wake | Accepted | 2026-09-06 |
| 0214 | Wake subsystem — WitDB broker | Proposed | 2026-09-06 |
| 0215 | cdp_where — сводка текущей точки | Proposed | 2026-09-06 |
| 0216 | F# anchor & diagnostics comfort parity | Proposed | 2026-09-06 |
| 0217 | goto symbol graph | Proposed | 2026-09-06 |
| 0219 | Habitat state consolidation — one witdb per root | Draft | 2026-09-07 |
| 0220 | In-Generation Procedural Execution | Draft | 2026-09-08 |
| 0221 | CDP Virtualization — per-line instances | Draft | — |
| 0222 | Configuration management — один TOML | Draft | 2026-09-10 |
| 0223 | Ship — слот из immutable-снимка (stage 3 0209) | Accepted | 2026-09-10 |

> **Примечание:** статусы нормализованы 2026-09-10 к единому inline-формату `**Status:** Accepted|Proposed|Draft` (суффиксы реализации сохранены). Реестр фиксирует фактическое состояние.

---

## 6. Известные разрывы ADR ↔ реальность

| # | Разрыв | Когда обнаружен | Статус |
|---|---|---|---|
| 1 | ~~**8771 в HTTP.sys (PID 4 System), healthz молчит.**~~ Gatekeeper переведён на Kestrel direct bind (2026-09-12); orphaned HTTP.sys снят. | 2026-09-10 (прод) | **закрыто (Kestrel tower 2026-09-12)** |
| 1b | **WitDB slots.witdb — один engine на процесс.** dbhub `witdb-bridge` держит файл открытым → CdpService Upsert и gatekeeper Fresh блокируются/падают. | 2026-09-12 | открыто (dbhub per-query open или sidecar cache) |
| 2 | **`[service] install_dir` закомментирован** в `D:\cdp-mcp\cdp-mcp.toml` → ensurer не автозапускает вышку/слот при cold boot (ADR-0203). | 2026-09-10 | открыто (ADR-0222 адресует) |
| 3 | **`port=8771` в конфиге слота — мёртвый ключ** (слот берёт `PickFreePort()`). ADR-0209 не упоминает этот ключ. | 2026-09-10 | открыто (ADR-0222) |
| 4 | **Вышка не читает toml вовсе** — stateless, реестр из witdb. Это по-документу, но конфиг-карта из трёх toml не описывает вышку. | 2026-09-10 | открыто (ADR-0222) |
| 5 | ~~**apply промоутил по живому корню** (`robocopy /MIR` в `D:\cdp-service` под работающим слотом) — инцидент exit=11 после 359-сек промоута; заборы (deploy.lock TTL 5мин без heartbeat + job lease) промоут переживал. Stage 3 ADR-0209 в коде отсутствовал (режима ship не было).~~ | 2026-09-10 | **закрыто (ADR-0223)**: apply/ship стартуют слот из immutable-снимка; live синкается по мёртвому; prefix-фикс `.staging` vs live |

---

## 7. Куда движемся

- **ADR-0222 (Draft):** единый TOML, секции по ролям, одна схема парсинга, без env-hell. Поглотит разрывы 2–4.
- **Вышка на Kestrel (обсуждение 2026-09-10):** единый стек слушателей устранит orphaned HTTP.sys-registration как класс (разрыв 1). Вопрос открыт — zero-downtime сохраняется, слоты не трогаются.
- **Реестр ADR → pre-commit hook** (секция 1): каждый новый ADR обязан иметь строку в реестре — проверка автоматизируется.