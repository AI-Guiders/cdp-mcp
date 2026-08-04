# Domain card: IdeSeeChannel (agent vision)

- id: `see`
- organ: `cdp_see` / `go=see`|`see_desk`|`vision`
- product: `#CDP`

## Invariants

- Loads local `path=` / `file=` or `url=` (http(s)|file://) → bytes → `ToolMediaOutbox.TryAdd` → MCP `ImageContent`.
- Caps: outbox MaxImages=2, MaxBytesPerImage=2.5MB (same as take/PlantUML).
- Not Lynx (text browser). Not capture (webcam). Not host-only Cursor Read as the habitat path.
- Always-ListTools Meta (not SoftOrgan-hidden) — discoverable vision organ.
- Optional HTTP cache under `.cdp/evidence/see-cache/` when ProjectRoot set.

## Entry

- `IdeSeeChannel.Handle` / `HandleJson` · Meta `cdp_see`
- Scene: `op=scene` · See: `path=`|`url=` (default when path/url set)

## Antipatterns

- Using Lynx / `cdp_browser` to "see" bitmaps.
- Expecting `take vision=true` for arbitrary PNGs — that opt-in is PlantUML/take only; `cdp_see` is the dedicated attach.
- SoftOrgan/Glass latch zoo for a one-shot vision Meta.

## last_ship

- 0.5.662: `cdp_see` path=|url= → ImageContent; go=see|see_desk|vision; World dig charge line; always-ListTools
