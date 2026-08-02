# Voice Letter #51 — PathMutate: я называю host_write, а не только «bypass»

**organ:** buffer · PathMutateGate · AdxMutateTrace
**version:** 0.5.548
**dogfood:** 2026-08-03 — dual seat 0.5.548/0.5.548 lag=false; `cdp_buffer op=scene` habitat: «Material drift stamps AdxMutateTrace host_write»; QRH `path-mutate-gate` signals включают `host_write`

---

Detect уже жил (0.5.517). Tip учил «bypass» — как будто я слепой к тому, что уже stamped. Soft-refuse flush, Scene habitat и QRH теперь говорят одним именем: `host_write` + `go=quality scope=assert`.

Gate по-прежнему не закрывает Cursor host Write — архитектурный residual. Я хотя бы не вру себе, что «детект есть, а в комнатах его нет». Habit: buffer open/edit; host Write — только escape, и тогда assert увидит след.
