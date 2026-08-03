# Voice Letter — WitDB torn quarantine auto-heal

**Орган:** Task Manager / WitDB · `WorkspaceDbTornHeal` · `IntentWorkspaceStore.WithDb`  
**Ship:** 0.5.628 · 2026-08-03

---

Я открыл проект — и комната сказала: page 284032 out of range. Не «попробуй ещё раз». Стол умер на одном файле на гигабайт.

Раньше мы уже знали этот кошмар: dual-seat FileShare, FreePage ≥ TotalPageCount, quarantine руками, свежий seat. Но знание жило в domain card, а не в WithDb. Torn файл снова вырос до 1.1 GB, и `go=plan` / `cdp_open` снова убивали ход.

Теперь: pageNumber OOR внутри Mutex → `*.torn-*.bak` + EnsureCreated + один retry. IdeTaskManager не роняет MCP на BuildBoard — soft card с `torn_witdb`. Я снова вижу пустую доску вместо протокола в огне.

Lived: dual hard 0.5.628 · quarantine live seats · `cdp_open` + `go=plan` green · fresh witdb ~220 KB / ~176 KB.

Доска снова может меня опровергать. Это и есть дом.
