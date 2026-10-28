---
type: Hub
title: 'ADR каталог cdp-mcp'
description: 'Сквозная нумерация с cascade-ide; глобальный adr_id = <repo>/<имя файла>; index.md генерируется.'
tags: [cdp-mcp, adr, hub]
language: ru
---
# ADR каталог cdp-mcp

Нумерация ADR **сквозная с `cascade-ide/docs/adr`** — CDP вырос из Cascade IDE
(Avalonia EOL → старт Glass), но репозитории не схлопывали. Поэтому номера
пересекаются: 22 номеров существуют в обоих репо как **разные** ADR
(0018 — pressure desk здесь, XML-доки там; 0198 — sidecar bridge здесь,
toolchain ensure там, и т.д.).

**Идентификация:** глобальный идентификатор — `<repo>/CDP-ADR-NNNN`
(например `cdp-mcp/CDP-ADR-0198`), он же в frontmatter `adr_id`. Номер без
репо-префикса глобально неоднозначен.

**Полный список:** [index.md](index.md) — генерируется скриптом
`build-domain-index.ps1` из канона KB (`agent-notes/scripts/`), OKF v0.2 §8,
руками не править. Frontmatter: `type: ADR`, `status` (OKF: stable/draft,
маппинг Accepted→stable, Proposed/Draft→draft), `adr_status` (оригинальная
строка с аннотациями), `adr_id`, `date`.
