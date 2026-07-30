# Priority — cockpit path peels (post ADR-0020)

## Done
- Desk-pulse for deferred softs (7890872)
- Glass spray skip on desk-pulse (c7abd4a / CDP-ADR-0020)
- seats_detail=full alone stays pulse (early refuse)
- Organ-only early exit (skip nav) for go=alert|chk alone
- FinishDeskPulse go-branch extract (PlanPulse.Go)

## Live dogfood
go=alert pulse OK. Avoid seats_detail=full as spray (it thrash-refuses). pane_full= still slow path.

## Later
cdp_organ Meta; quiet-chrome; root folder peel = low ROI
