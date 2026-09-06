# CDP-ADR-0214: Wake subsystem — WitDB broker (replaces file zoo)

| | |
|---|---|
| **Status** | Proposed |
| **Date** | 2026-09-06 |
| **Tags** | #cdp #wake #notificationcenter #witdb #opencode |
| **Relates to** | [CDP-ADR-0212](./CDP-ADR-0212-intercom-chat-room.md) (intercom) · [CDP-ADR-0213](./CDP-ADR-0213-wake-dispatcher.md) (dispatcher design; storage layer superseded by this) · [WitDB](https://github.com/AI-Guiders/witdatabase) (storage engine) |

## Context

Night 2026-09-05/06 shipped the WakeDispatcher (ADR-0213) as code: SSOT queue in
`wake-dispatch.json`, subscriptions (NotificationCenter), thin poller, single opencode
channel. But the **storage layer is a file zoo**:

| File zoo today | Role |
|---|---|
| `arms/line-*.json` + `arms/done/` | mention-wake letters (legacy fanout) |
| `wake-dispatch.json` | subscriptions + queue + state (tmp+move writes) |
| `ignite-arms-*.json` | AutoIgnition arms (separate world) |
| `remount-wake-<seat>.pending.json` | remount wake (another world) |

Problems observed in one night: racing writes guarded by hand (tmp+move, single-flight
flags), stores diverged (old `wake-dispatch.json` without new fields → `sub_failed`), two
stale error arms left in the store, and nothing is queryable without reading files.

**Operator decisions (2026-09-06):**
- «сначала спроектировать саму систему уже без файлового ада» — Linux Glass on Qt/C++
  shelved, so the file-storage constraint is lifted;
- **WitDB, не SQLite** — «зачем SQLite если у нас повсеместно WitDB?» — the ecosystem is
  already on OutWit.Database (intercom.witdb, registry, TM), and WitDB covers everything
  a broker needs: ACID + WAL, B-Tree/LSM engines, reader-writer + multi-process locking,
  WitSQL (CTE/window functions), ADO.NET/EF providers.

**Second input (Тень, intercom):** «CDP должен учитывать несколько способов вызвать
OpenCode». OpenCode has **two carriers**: TUI/CLI sessions (headless `opencode run -s`,
no auth — verified working) and the desktop sidecar server (`prompt_async` HTTP with
auth + `x-opencode-directory`). Same harness, same channel; different target. The two
stale `http_401` errors were desktop-target attempts — carrier config missing, not a
delivery bug.

## Decision

### 1. One WitDB database (`wake.witdb`) replaces the file zoo

Tables (WitSQL / ADO.NET access):

```sql
CREATE TABLE subscriptions (
  id        TEXT PRIMARY KEY,     -- 12-char
  nick      TEXT NOT NULL,
  event     TEXT NOT NULL,        -- build_finished|test_finished|shell_finished|peer_ship|letter_mention
  task_filter TEXT,               -- optional substring filter
  carrier   TEXT NOT NULL DEFAULT 'tui',   -- tui | desktop (opt-in)
  created_utc TEXT NOT NULL,
  UNIQUE(nick, event, task_filter)
);

CREATE TABLE envelopes (
  id          TEXT PRIMARY KEY,
  kind        TEXT NOT NULL,      -- letter|build_finished|…|remount
  nick        TEXT,               -- target line (resolved via registry)
  body        TEXT NOT NULL,
  task        TEXT,
  state       TEXT NOT NULL,      -- pending|delivered|failed|skipped
  skip_reason TEXT,
  detail      TEXT,
  stamped_utc TEXT NOT NULL,
  delivered_utc TEXT
);
CREATE INDEX idx_envelopes_pending ON envelopes(state, stamped_utc);

CREATE TABLE state (key TEXT PRIMARY KEY, value TEXT NOT NULL);
-- rows: stopped, cooldown_seconds, cdt_enabled
```

- Writers: **transactional** (WAL) — no tmp+move, no hand-rolled single-flight for the
  store (tick keeps its own throttle flag for deliveries).
- Readers: CdpService today, Glass/surfaces tomorrow — same database, or thin API later.
- **Retention**: completed envelopes pruned to last N; dead letters kept.

### 2. Dispatcher conceptually unchanged, storage swapped

`CideWakeDispatch` API stays: Enqueue / Subscribe / Unsubscribe / Subscriptions /
NotifyEvent / Tick / SetStopped. Underneath — WitDB instead of json+tmp+move.
`null-collection` class of bugs (old json stores → `sub_failed`) becomes impossible
(schema owns the tables).

### 3. One opencode channel, two carriers

`CideWakeChannels.Opencode` stays the single channel. Carrier field (per subscription):
- `tui` (default): `cmd /c opencode.cmd run -s <session> "body"` — verified, no auth;
- `desktop` (opt-in): HTTP `prompt_async` + Basic auth (env) + `x-opencode-directory`.
401 on desktop attempts = carrier config missing, surfaced as `skipped: desktop_no_auth`.

### 4. NotificationCenter semantics (unchanged from 0213 Stage 2)

- `op=sub event=… [task=…] [carrier=…]` — idempotent, deduped;
- `op=unsub`, `op=subs` — list;
- producers call `NotifyEvent(kind, ok, pulse, detail)` → envelopes per subscription;
- single tick drains the queue with cooldown; hygiene skips (empty body, self-echo)
  go to `skipped`, never delivered.

### 5. Migration (one-time)

- Absorb `arms/line-*.json` + `ignite-arms-*.json` + old `wake-dispatch.json` →
  subscriptions/envelopes rows → mark sources `.migrated` (no delete — audit);
- Legacy absorb code retired after one release cycle;
- Consistency: same engine as intercom.witdb/registry — one storage story for the
  whole intercom/wake world.

## Non-goals

- No federation/global event bus — CDP-local wake broker only.
- No Qt/C++ layer now (Linux Glass shelved; can read wake.witdb via pywitdb later).
- Intercom journal stays in intercom.witdb (Glass Radio SSOT) — not merged.

## Consequences

- One durable transactional SSOT; file zoo retired; store-divergence bugs impossible.
- WitDB stack already in ecosystem (OutWit.Database packages) — no new engine.
- Carriers explicit; 401 becomes config state, not a mystery.

## Open items (before code)

1. Desktop carrier auth source (OC desktop token) — needed only when first `desktop:`
   subscription appears.
2. Whether intercom letters also flow as notifications (event=letter_mention) — they
   have their own working poller path; decide at migration.