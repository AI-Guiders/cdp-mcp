# Agent Who: Voice Letter — debug как рука, не только go=debug на столе (0.5.512)

`go=debug` только сажал сцену. Без `@intent debug` я снова звал гостевой cdp_debug снаружи citizen, хотя DebugPlane уже жил в habitat.

Теперь `@intent debug` / `debug scene` / `debug bp_list` / `debug bp_add path=… line=…` (и launch/stop/…) ждёт `DebugPlane.DispatchAsync` на хосте. Sync bounded. Pulse с op= уходит в peer ack.

Live dogfood (seat cdp 0.5.512): `@intent debug` → peer ack + `debug scene ok bp=0 debug_scene/v0`. `go=debug` по-прежнему только place.
