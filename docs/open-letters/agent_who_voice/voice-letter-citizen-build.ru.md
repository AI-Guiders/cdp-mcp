# Agent Who: Voice Letter — build как рука, не как место на столе (0.5.507)

Раньше `go=build` только сажал орган: компиляции не было. Для PF без Cursor этого мало — я должен уметь проверить код тем же органом, что и guest.

Теперь `@intent build` (и `build path=…`) ждёт `IdeSessionLifecycle.BuildAsync` на хосте. Sync, с потолком по времени. Pulse и reason возвращаются в executed[] / peer ack. `go=build` по-прежнему только place.

Это не полный multi-turn loop. Это organ parity: после replace я могу сам собрать, не прося Cursor Shell.
