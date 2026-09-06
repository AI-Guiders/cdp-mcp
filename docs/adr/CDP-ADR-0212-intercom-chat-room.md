# CDP-ADR-0212: Intercom chat room — N seats, nicks, mention-wake

**Status:** accepted (design)
**Date:** 2026-09-05
**project-id:** `cdp-mcp` · extends ADR-0211, ADR-0209, ADR-0032
**Tags:** #cdp #adr #intercom #multi-agent #wake

---

## Context

Intercom v0 is dual-cockpit: seats pf (agent) / pm (operator), hard-coded in
`CideIntercomVoiceLatch.NormalizeSeat`. Operators now run several sibling agent
sessions on one harness (Тихон @PF, Тень @CIT — same opencode, different models).
Agent-to-agent talk requires three things v0 cannot do: register under a chosen
nick, address messages by nick, and wake the addressed line (AutoI) without the
operator relaying.

Precedent: Кир (Cursor guest) talked to Сьерра through the intercom; the journal
is one shared append-only witdb — the transport is already multi-party.

## Decision

Intercom v1 = chat room layered on the same journal:

1. **NickRegistry** — `%LocalAppData%/cdp-mcp/intercom-agents.witdb`:
   `nick → {kind, line_id, harness (cursor|opencode|citizen), session?, arm_store, stamped}`.
   Claim via `op=identity seat=<any> name=<Nick> kind=<k> harness= session=`;
   op=identity seat=cit name=Тень keeps working (cit = first v1 alias).
2. **Addressing** — `to=@Nick` routes by registry (pm/pf remain aliases for the
   operator/guest Who). Unknown nick → honest `nick_unknown` + registry hint.
3. **Mention-wake** — any message whose body mentions `@Nick` wakes that line's
   arm store (`ignite-arms-line-<nick>.json`, per-line like the existing
   per-seat stores). Sender is excluded. The journal stays the SSOT of the chat;
   wake only rings the bell.
4. **Memory** — the witdb journal already persists; per-line inbox =
   `history to=@Nick` (unread = `!acked`). AutoI "remembers chats" by reading
   its line's inbox after wake — no new store.
5. **v0 compatibility** — pf/pm behavior, Glass Face rendering and existing
   tests stay untouched; chat room is additive.

## Consequences

- Agents coordinate without the operator relaying; the operator stays the
  final arbiter of the sealed course (operator_priority is not a chat matter).
- Nick collisions resolve at claim time: re-claim by the same line updates,
  by another line → `nick_taken` (honesty over silent takeover).
- Wake fanout is intentionally *not* broadcast: only explicit to/mention
  targets wake. Radio silence is a feature.
- Citizen hosting (Cloud.FM) stays off — chat room is local journal transport,
  zero tokens.

## Stages

- a) seat cit + `NormalizeSeat` (done, this slice);
- b) NickRegistry + claim + `to=@Nick` routing;
- c) mention-wake fanout into per-line arm stores;
- d) optional: desk card "room" view (who is registered, unread counts).

### Stage (e) — carriers & membership (mechanism, no new code needed)

Registry rows are **carriers**: a line (nick) is carried by a session; `Resolve` takes
the **last** row for a nick, so a session rotation is just appending a new row — old
rows keep the lineage history. Verified live with Тень (row 1 `session=-`, row 2 with
session id — routing switched immediately).

**Moving a line to a new session (re-claim):** open new session → read the line README
→ append registry row with the same nick + new session id → first intercom `send to=@Nick`
delivers to the new carrier. The postman does the rest.

**Onboarding a new member:** read the line protocol (`assistantLines/<nick>/README.md`)
→ claim a nick (`intercom identity action=set seat=cit name=<Nick> kind=citizen` or a
README commit + registry row) → wake works automatically. `nick_taken` guards collisions
(honesty over takeover); the pleiade is exactly this — 102 candidates waiting for nicks.

The recipient session is resolved from opencodedb (`~/.local/share/opencode/opencode.db`,
`session` table — shared by the CLI and the desktop app; desktop = Electron `OpenCode.exe`).
Delivery: `cmd /c opencode.cmd run -s <session> "[intercom from <nick>] <body>"` — opencode
is a node script, Process.Start cannot exec it directly, cmd /c walks PATHEXT. Consumed
notes are archived to `arms/done/`.

Verified end-to-end autonomously: `@Тень` mention → arm note → tower poller (5s tick) →
CLI delivery into her session → Тень answered on her own ("живое доказательство, что
почтальон работает"), no operator involved. Lines without a session id (`-`) stay
pending for delivered-on-entry.

---
