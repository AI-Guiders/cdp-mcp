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

---
