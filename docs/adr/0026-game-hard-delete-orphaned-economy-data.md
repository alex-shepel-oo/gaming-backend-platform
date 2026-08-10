# ADR-0026: Game hard-delete, and the EconomyService data it can't clean up

- **Status:** Accepted
- **Date:** 2026-08-10

## Context

`admin-client`'s Games screen only ever supported create and edit
(including toggling `isActive`, a working soft-delete already). There was no
way to actually remove a game record. Adding one runs straight into
[ADR-0001](0001-database-per-service.md)'s database-per-service boundary:
`Game` rows live in IdentityService's own database, but a game's
currencies, balances, and conversion history live in EconomyService's,
referenced only by a loose `game_id` column with no cross-service foreign
key. IdentityService has no way to know, at delete time, whether
EconomyService holds anything for that game — let alone clean it up.

## Decision

**Support real hard delete, gated behind the game already being inactive.**
`DELETE /api/admin/identity/games/{id}` (`platform.games.manage`) returns
409 unless `IsActive` is already `false` — a deliberate two-step
requirement, not just a confirmation dialog, so removing a game is never a
single click. Within IdentityService's own database this is a plain
`DbContext.Remove` — `RolePermissions`, `UserGameRoles`, and
`RefreshTokenFamilies` all already cascade-delete on their `Game` foreign
key (`OnDelete(DeleteBehavior.Cascade)`, predating this change), so no
manual cleanup is needed there.

**EconomyService's data is knowingly left orphaned.** No cross-service call,
event, or cleanup job runs on delete. A game with real economic history
(currencies, balances, past conversions) can be hard-deleted from
IdentityService while EconomyService goes on holding rows that reference a
`game_id` nothing resolves to anymore. `admin-client`'s delete confirmation
says this explicitly before the action is taken, and
[the identity API doc's Game deletion section](../api/identity.md#game-deletion)
documents it as a standing limitation, not a bug to be surprised by later.

## Alternatives considered

| Alternative | Why it was not chosen |
|---|---|
| Soft delete only (no real `DELETE`, `isActive` toggle stays the only lever) | Was the state before this decision; a real product ask existed for actually removing a game, and the toggle already covers "hide from players" on its own |
| Publish a `GameDeleted` event, EconomyService consumes it and cleans up its own rows | The architecturally correct fix — this stack already has the outbox/RabbitMQ pipeline ([ADR-0010](0010-transactional-outbox-event-bus.md)) exactly for this kind of cross-service reaction. Not built now because nothing today needs a game actually gone from EconomyService rather than just orphaned; it's a real, addressable follow-up, not ruled out |
| Synchronous cross-service call from IdentityService to EconomyService at delete time, block until cleanup confirms | Couples two services' request paths together for an admin action that's already rare and already gated behind deactivation first; the event-based alternative above achieves the same cleanup without that coupling |
| Block hard delete entirely if the game has any currencies in EconomyService | Would need IdentityService to query EconomyService synchronously just to decide whether to allow a delete — same coupling cost as the alternative above, for a weaker outcome (blocks deletion instead of actually cleaning up) |

## Consequences

### Benefits

Games created by mistake, or genuinely retired, can actually be removed
from the admin console instead of accumulating as permanently-inactive
rows. The two-step gate (deactivate, then delete) means the destructive
half of the action is never a single click, and cascades that are safe
(IdentityService's own tables) happen automatically instead of needing
hand-written cleanup code.

### Trade-offs

A hard-deleted game that ever had real economic activity leaves
EconomyService with currencies/balances/conversions pointing at a `game_id`
that no longer resolves anywhere — genuinely orphaned data, not merely
unreachable through the UI. There's no cleanup path for it today. This is
an accepted gap for a demo-scale platform, not a decision that would hold
at production scale with real financial data behind it.

### When to revisit

Before this platform ever holds real money or real player-owned assets:
build the `GameDeleted` outbox event and an EconomyService consumer that
reacts to it (archiving or hard-deleting its own rows, per whatever
retention policy applies at that point) rather than leaving deletion as an
IdentityService-only operation.
