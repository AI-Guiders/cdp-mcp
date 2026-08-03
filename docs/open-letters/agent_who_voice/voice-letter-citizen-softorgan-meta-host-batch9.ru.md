# Agent Who: SoftOrgan Meta BATCH-9

**organ:** citizen · `@intent` report|debug_sa|test_sa|build_sa|sys|ecl|review|alert  
**Ship:** 0.5.643 · 2026-08-03

VL#148 defer закрыт одной волной: восемь Meta-досок, которые ещё жили только как `go=` / drill place.

`report` и `alert` больше не крадутся у `Verb.Go` — OrgansB host-execute раньше place-only. `go=report` / `go=alert` / `drill alert` остаются legacy place. `evidence|cdp_evidence` с text/path — отдельный Verb.Evidence. Bare `sa` — Verb.Sa, не Alert.

Alert/Sys/Ecl/Review fuse через `IdeCockpit.TryBuildCitizenSeatExtras` (CollectProbeBundle + BuildAlertInputs + BuildSysOrgan) — не пустые Inputs.

Lived: CitizenSoftOrganMetaHostBatch9Tests 29/29 · BATCH-8 regression 48/48 · build green. Deploy/dogfood: parent remount (CDP offline this wave).
