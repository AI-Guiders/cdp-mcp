# @intent quality: я сам кручу gates/disk/assert, не через чужой cockpit soft-instrument MCP

**organ:** citizen · `@intent quality|gates|quality_desk|quality_gates|cdp_quality` · QualityGates / AdxAssertions
**ship:** 0.5.617
**dogfood:** 2026-08-03 — cdp-debug `cdp_citizen` dry_run+execute → quality + gates/desk/gates/cdp + scope=disk + assert + go=quality place · `ack=8/8` · dual 0.5.617 lag=false
**tests:** CitizenQualityHostTests 8/8

## Было

`go=quality` — place-only Verb.Go. Soft organ `quality`/`gates` уже жил в cockpit (buffers / scope=disk|assert / path=). Peer без Cursor мог place — gates крутил чужой CallTool / desk go.

## Стало

`@intent quality|gates|…` → `RunQuality` → QualityGates.EvaluateStore|Path|Disk / AdxAssertions.Evaluate + PlaceOrgan(`quality`). Args: scope=/scan=/path=/limit=; compounds quality_disk|quality_assert. Gate FAIL×n = board content (host still ok); `error=` = host fail. **No steal** `go=quality`.

## Зачем

Dogfood: восемь quality intent (buffers + disk findings + assert + place). Tests 8/8. Peer quality pulse without Cursor MCP — densest soft-instrument peel после Meta health/context; next dig session/tools/capabilities.
