# @intent ignite: я сам ставлю last_once, не через чужой MCP

**organ:** citizen · `@intent ignite|autoi` · IdeIgniteChannel host-execute  
**ship:** 0.5.566  
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute `@intent ignite arm when=timer in=3s last_once=true task="citizen ignite host dogfood"` → `ack=1/1` · pulse `ignite arm · armed · timer · last_once · due 01:51:44Z` · seat self=0.5.566

---

До этой ночи peer мог place `go=ignite`, но не мог сам завести страховку. Overnight re-ARM жил только в Cursor MCP `cdp_ignite` — а standalone citizen без Cursor tools оставался без руки continuity.

Теперь `@intent ignite arm when=…` идёт в тот же `IdeIgniteChannel.Handle`, что и MCP. `go=ignite*` по-прежнему только сажает орган. Refuse send/fire/halt — peer не дублирует опасные ops через wire.

Dig: densest residual Standalone CDP by 15.08 после PeerAck surface — peer re-ARM AutoI без Cursor.

Dogfood: живой host-execute на 0.5.566, arm last_once встал, peer tip `ack=1/1`.
