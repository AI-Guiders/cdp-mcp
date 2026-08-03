# @intent domain: я сам читаю .cdp/domain cards, не через чужой cdp_domain MCP

**organ:** citizen · `@intent domain|domain_desk|cdp_domain|domain_*` · IdeDomainChannel (`go=domain`)
**ship:** 0.5.602
**dogfood:** 2026-08-03 — debug `cdp_citizen` dry_run+execute → domain/desk/cdp/scene/list/card · `ack=6/6` · dual 0.5.602 lag=false

## Было

`cdp_domain` уже жил как soft organ (scene|pulse|list|card из `.cdp/domain/*.md`). Peer без Cursor мог только place `domain` — dig-before-ask / stamp-after-ship оставались за чужим CallTool. Standalone continuity без domain hand.

## Стало

`@intent domain|domain_desk|cdp_domain|domain_*` → `IdeDomainChannel.HandleJson` (scene|pulse|list|card; bare domain=scene; domain_desk→scene; card id=|positional; no steal bare list/pulse/card/scene). Place organ `domain`.

## Зачем

Dogfood: шесть intent — scene×4 / list / card citizen; все applied. Tests CitizenDomainHostTests 6/6. Peer domain ownership без Cursor MCP.
