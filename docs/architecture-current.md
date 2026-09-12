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
| **Вышка (gatekeeper)** | `CdpGatekeeper.exe` | маршрутизация 8771 → активный слот | **Kestrel (direct bind)** | 8771 | `[tower]` via `--config` (same SSOT as bridge) | **Kestrel с 2026-09-12** |
| **Сервис (слот)** | `CdpService.exe` | весь CDP-функционал | Kestrel | 8772+ (PickFreePort) | `--config` (bridge SSOT `D:\cdp-mcp\cdp-mcp.toml`) | жив |
| **Ensurer** | в мосте | воскрешение вышки/слота при провале | HTTP probe | — | `[slots].install_dir` + `[bridge].auto_start_slot` | **включён (0222 slice A)** |

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

**Два разных стека слушателей — спроектировано** (ADR-0209): вышка и слоты оба на **Kestrel** с 2026-09-12 (tower migration закрыла HTTP.sys gap #1). Разные жизненные циклы: слот-порт освобождается с процессом; вышка 8771 — вечная.

---

## 4. Конфиг-файлы (ADR-0222 — shipped 2026-09-12)

| Файл | Читает | Роль / секции |
|---|---|---|
| **`D:\cdp-mcp\cdp-mcp.toml`** | **мост** (SSOT), **ensurer→слот**, **вышка** `[tower]`, **deploy/Start-CdpService** | `[tower]` `[slots]` `[bridge]` |
| `D:\cdp-service\cdp-mcp.toml` | hardlink → SSOT (`Sync-CdpConfigSsot.ps1`) | не отдельная копия |
| `D:\cdp-gatekeeper\cdp-mcp.toml` | hardlink → SSOT (опционально) | не отдельная копия |
| `%LocalAppData%\cdp-mcp\slots.witdb` | вышка + слот | реестр слотов (CdpSlotRegistry) |
| `%LocalAppData%\cdp-mcp\cdp-state.witdb` | слот | runtime-state SSOT (ADR-0219) |

**Канон:** EmbeddedTOML defaults в бинарнике + operator overlay (`cdp-mcp.toml` — только diffs). Env для seat-политики **запрещены** (0222). Ops remount = `--bridge-rev` в `mcp.json` args (0224), не env.

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
| 0222 | Configuration management — один TOML | Accepted | 2026-09-10 |
| 0223 | Ship — слот из immutable-снимка (stage 3 0209) | Accepted | 2026-09-10 |
| 0224 | Env-free ops — CLI + EmbeddedTOML, no operator env | Accepted | 2026-09-12 |

> **Примечание:** статусы нормализованы 2026-09-10 к единому inline-формату `**Status:** Accepted|Proposed|Draft` (суффиксы реализации сохранены). Реестр фиксирует фактическое состояние.

---

## 6. Известные разрывы ADR ↔ реальность

| # | Разрыв | Когда обнаружен | Статус |
|---|---|---|---|
| 1 | ~~**8771 в HTTP.sys (PID 4 System), healthz молчит.**~~ Gatekeeper переведён на Kestrel direct bind (2026-09-12); orphaned HTTP.sys снят. | 2026-09-10 (прод) | **закрыто (Kestrel tower 2026-09-12)** |
| 1b | ~~**WitDB slots.witdb — один engine на процесс.**~~ dbhub lazy witdb держал файл после первого query → CDP Upsert блокировался. | 2026-09-12 | **закрыто (2026-09-12):** dbhub `releaseLazyWitdbSource` после tool call + `readonly` на `execute_sql` cdpSlots (не на `search_objects` — dbhub TOML запрет) |
| 2 | ~~**`install_dir` закомментирован**~~ → `[slots].install_dir` + `auto_start` в SSOT; ensurer передаёт bridge `--config`. | 2026-09-10 | **закрыто (0222 slice A 2026-09-12)** |
| 3 | ~~**`port=8771` в конфиге слота — мёртвый ключ**~~ → `MapSlot` Port=0 (PickFreePort); TOML `port` только tower legacy. | 2026-09-10 | **закрыто (0222 slice A 2026-09-12)** |
| 4 | ~~**Три копии toml / вышка без секции**~~ → SSOT `D:\cdp-mcp\cdp-mcp.toml` + `[tower]`/`[slots]`/`[bridge]`; gatekeeper читает `[tower]`. | 2026-09-10 | **закрыто (0222 slice A 2026-09-12)** |
| 5 | ~~**apply промоутил по живому корню** (`robocopy /MIR` в `D:\cdp-service` под работающим слотом) — инцидент exit=11 после 359-сек промоута; заборы (deploy.lock TTL 5мин без heartbeat + job lease) промоут переживал. Stage 3 ADR-0209 в коде отсутствовал (режима ship не было).~~ | 2026-09-10 | **закрыто (ADR-0223)**: apply/ship стартуют слот из immutable-снимка; live синкается по мёртвому; prefix-фикс `.staging` vs live |

---

## 7. Куда движемся

- **ADR-0219 (Draft):** habitat state consolidation — one witdb per root.
- **Реестр ADR → pre-commit hook** (секция 1): каждый новый ADR обязан иметь строку в реестре.