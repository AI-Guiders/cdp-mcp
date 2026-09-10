# CDP-ADR-0223: Ship — слот из immutable-снимка (реализация stage 3 ADR-0209)

**Status:** Accepted  
**Date:** 2026-09-10  
**project-id:** `cdp-mcp` · extends ADR-0209 (вышка+слоты), ADR-0211 (deploy-plane ownership), ADR-0203 (deploy gap)  
**Tags:** #cdp #adr #deploy #slots #ship #immutable

---

## Context

Инцидент 2026-09-10: `cdp_deploy mode=apply` упал с `robocopy promote failed exit=11` после **359-секундного** промоута. Разбор показал, что заплаты ADR-0211 (worker-clone, deploy.lock, kill-verify-retry) латают симптомы модели, которую ADR-0209 уже упразднил:

- **Промоут по живому корню.** `Apply` писал `robocopy /MIR` прямо в `D:\cdp-service`, из которого работал слот (PID 38904). Окно гонки = вся длительность robocopy.
- **Заборы протухают.** `deploy.lock` — TTL 5 мин по mtime без heartbeat; promote в 6 минут пережил забор. Job lease протух так же (ADR-0211 зафиксировал это на lease).
- **Разрыв конфигурации.** `[service] install_dir` закомментирован (architecture-current §6 #2) → `IsDeployLockFresh` у моста слеп → suppress висел на одном протухшем lease.
- **Частичный /MIR.** exit=11 оставил live в смеси версий (pdb от 10.09 рядом с dll от 09.09); сервис продолжил работать на смеси.

При этом stage 3 ADR-0209 («деплой = запустить ещё один процесс; промоушен упраздняется») в коде **отсутствовал**: ни `mode=ship`, ни старта слота из staging.

## Decision

Деплой сервиса — это запуск процесса, а не перезапись каталога:

```
publish в .next (как soft, живых не касается, минуты — без лока)
→ Move .next → {ServiceInstall}.staging/<yyyyMMdd-HHmmss>_<guid>   (тот же том, атомарно)
→ deploy.lock Acquire                     (короткая критическая секция)
→ старт слота из снимка на pinned порту (CDP_SLOT_PORT)
→ verify: прямой /healthz на :port (fail ⇒ kill нового, старые не тронуты, pending re-pointed на снимок)
→ retire старых слотов из live/staging (kill → verify dead; keepPid = новый)
→ sync live из снимка robocopy — по гарантированно мёртвому каталогу
→ deploy.lock Release → prune снимков (keep 3)
```

- **`mode=ship`** — полный цикл (publish + активация) за один вызов.
- **`mode=apply`** — тот же `ActivateSnapshot`, но из уже staged `.next`; bridge-волна без изменений (deferred, remount-on-change).
- **Live-корень** = last-good образ для cold boot; обновляется только когда под ним никто не бежит (`SyncLiveIfIdle`); занят (напр. caller) — пропускается без фейла, догонит следующий ship.
- **Rollback** = старт из прежнего снимка (immutable на диске); тёплый процесс-резерв не нужен.
- **Prefix-фикс:** сравнения по `ServiceInstall` теперь path-segment safe (`root\`) — `.staging` соседи больше не матчатся под live (guard ADR-0211, `StopLockHoldersUnder`).
- **Tree-kill fallback (найдено первым живым ship):** CDP shell-табы — дети текущего слота; воркер, запущенный из таба, находится в дереве retire'имого слота, и `Kill(entireProcessTree)` отказывает («tree containing the calling process»). Retire падает обратно на простой `Kill()` — дети слота (ts-worker) осиротевают и добиваются prune'ом позже. В прод-пути (durable supervisor) воркер не потомок слота — tree-kill работает.

Замечание про lock: heartbeat не вводится — критическая секция (start→verify→retire→sync) ~десятки секунд, TTL 5 мин покрывает с запасом. Publish остаётся вне лока.

## Consequences

**+** Класс `exit=11` мёртв конструктивно: промоута по живому каталогу не существует.  
**+** Фейл verify бесплатен: старый слот не тронут, pending переуказан на снимок — retry безопасен.  
**+** `Apply` и `Ship` делят один хвост (`ActivateSnapshot`) — единственный путь активации.  
**−** In-flight запросы на retire'нутом слоте рвутся (принято в ADR-0209).  
**−** Снимки занимают диск (retention: 3).  
**−** `install_dir` для cold boot всё ещё нужен (разрыв #2 не закрыт, но его blast radius упал: перезапуск больше не гонится с промоутом).

## Verification

- `CdpDeployShipTests` (15 фактов): алиасы парсера, аллокация снимка, move, предикат `ShouldRetire` (включая prefix-ловушку `.staging`), `IsPortFree`, `ResolveTarget` для ship, dry-run план (stage publish, без bridge).
- Live (2026-09-10, три ship подряд): новый слот healthy на pinned порту за ~24с целиком (vs 359с фейл выше); вышка `tower_pid` не изменился; прежние слоты retired (второй ship доказал tree-kill отказ → fallback добавлен; третий ship убрал оба прежних); `live == snapshot` по длине+mtime exe; pending отсутствует; retention = 3 снимка; `deploy.lock` отпущен. `cdp_test` по фильтру ship — 15/15 через мост→вышку→новый слот.

## References

- `Cdp.Deploy/CdpDeployShip.cs` (снимки, retire, sync, prune)
- `Cdp.Deploy/CdpDeployOrchestrator.cs` (`Ship`, `ActivateSnapshot`, ship-path в `Apply`)
- `Cdp.Deploy/CdpServiceControl.cs` (`StartSlotFromDir`, prefix-фикс)
- `CdpServiceHost.ResolveSlotPort` (`CDP_SLOT_PORT`), `CdpSlotRegistry.IsPortFree`
