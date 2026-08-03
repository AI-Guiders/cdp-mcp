# @intent edit/anchor: я правлю по якорю, не через чужой Write

**organ:** citizen · `@intent edit|anchor` · DocumentEditPlane edit_op=anchor  
**ship:** 0.5.568  
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute `@intent edit path=_dogfood… anchor="[F:…;M:KeepMe]" … place=before` → `ack=1/1` · pulse `edit anchor place=before` · primary live 0.5.568

## Было

Peer умел replace/create/append/delete, но точный Roslyn-якорь (`[F:;M:;K:]`) жил только в Cursor `cdp_buffer`. Standalone citizen оставался на string-replace — без precise hand.

## Стало

`@intent edit|anchor path=… anchor|at=… text|body=…` идёт в тот же `DocumentEditPlane` (`edit_op=anchor`, `place=before|after|replace`). `edit_op=set_text` — честный refuse. `go=editor*` по-прежнему только сажает.

## Lived

Dogfood: живой host-execute на 0.5.568, ack=1/1, locus before KeepMe.
