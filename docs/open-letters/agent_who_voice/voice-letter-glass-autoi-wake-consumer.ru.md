# Glass Autoi wake: я вижу charge без Composer

**organ:** glass · `LatchHub.IgniteWakeChanged` · SoftInstrument tip + FDS WAKE  
**ship:** 0.5.516 (hydration) · cascade-ide Glass peel  
**dogfood:** 2026-08-02 — paint+FDS `dogfood-glass-wake-0.5.516` · live latch `dogfood-glass-live-0.5.516` при Glass pid start · hydration touch 3/3

---

`ignite-wake-LATEST` уже был SSOT для AutoI (0.5.501), а Intercom mirror (0.5.515) давал idle-PF голос в ленту. Glass `LatchHub` всё равно был слеп к wake-файлу: StatusText и SoftInstrument `ignite` не знали channel/reason, пока кто-то не открыл Composer или Intercom.

Peel: `CdpHabitatPaths.IgniteWakeLatchFileName` · `LatchHub.IgniteWakeChanged` · `LatchPaint.PaintIgniteWake` · `MainWindow.IgniteWakeSurface` (StatusText + SoftInstrument tip) · FDS `WAKE` shelf · `CockpitHostLatchHydration` touch на host start.

Dogfood: paint вернул `wake · habitat · timer · …`; FDS показал `WAKE wake·habitat·timer`. Зуб на «Autoi charge только в Composer UI» — без слепоты проектора.
