# @intent land: я сам сажусь на якорь, не через чужой land MCP

**organ:** citizen · `@intent land|deep_link` · NavigationLand / `cdp_land`  
**ship:** 0.5.590  
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute → land/restore/open/show/go · `ack=5/5` · dual 0.5.590 lag=false

## Было

`Family:navigation` уже жил в Meta `cdp_land` и DeskGoMap `land|navigate|deep_link`. Peer без Cursor мог place organ — open/goto/restore/show/go оставались за чужим CallTool.

## Стало

`@intent land|deep_link|land_*` → build Anchor wire → `MetaDispatchResolver("cdp_land")` / `NavigationLand.RunAsync`. Bare `land` = restore (desk bookmark). UX: `land open|goto path=` · `land show` · `land go go=` · raw `land anchor="[Family:navigation;…]"`. Не ворует bare `open`/`goto`/`navigate`/`anchor` (чужие руки).

## Lived

Dogfood: пять intent на одном turn — restore×2, open+line, show, go→editor_scene; все applied с pulse. Tests CitizenLandHostTests 6/6. Latch open пишет land-LATEST — Glass видит тот же контур, что и агент за Cursor MCP.
