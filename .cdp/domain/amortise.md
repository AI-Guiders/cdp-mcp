# amortise — ListTools roundtrip (L4)

**Tags:** #cdp #listtools #amortise #axb

## pulse

Repeat ListTools at same `capabilitiesRev` must not recompose full tools[] (bridge + service caches).

## SSOT

- ADR: `docs/adr/CDP-ADR-0230-listtools-amortisation.md`
- Bridge: `CdpBridgeToolsCache` · invalidate on watcher rev
- Service: `VisibleToolsSessionCache` · fingerprint `(rev,phase,object,language,intent)`
- Meta: `MetaToolCatalog.All` once per process
- Soft-hide: `VisibleToolCatalog.SoftInstrumentMetaNames` (+ ignite/pressure/…)
- Optional: `GET /api/v1/cdp/capabilities?rev=N` → `unchanged:true`

## last_ship

- 2026-09-19T04:07Z L4 ListTools amortisation + soft-hide + ADR-0230
