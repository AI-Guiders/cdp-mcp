# @intent pkg: я сам трогаю NuGet, не через чужой pkg MCP

**organ:** citizen · `@intent pkg|nuget|packages` · MetaDispatch `cdp_pkg_*`
**ship:** 0.5.591
**dogfood:** 2026-08-03 — debug `cdp_citizen` dry_run+execute → pkg/list/nuget/find/outdated · `ack=5/5` · dual 0.5.591 lag=false

## Было

`cdp_pkg_list|find|add|remove|update|outdated` уже жили в Meta + PackageOps. Peer без Cursor мог place organ — list/find/outdated оставались за чужим CallTool.

## Стало

`@intent pkg|nuget|packages|package|pkg_*|nuget_*` → `MetaDispatchResolver("cdp_pkg_*")`. Default bare `pkg`/`nuget` = list. UX: `pkg find query=` · `pkg outdated` · `pkg add|remove|update id=` (id required). `take=` идёт Number (`PutIntIfPresent`), не String. Не ворует bare `find` (IdeFind).

## Lived

Первый dogfood: find с `take=3` упал на JSON Number vs String — живой мир поймал то, что unit с GetString пропустил. После фикса: пять intent на одном turn — list×3, find nuget:3 xunit, outdated; все applied. Tests CitizenPkgHostTests 6/6. Peer трогает packages без Cursor `cdp_pkg_*`.
