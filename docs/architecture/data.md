# Data ownership

Database-per-service ([ADR 0001](../adr/0001-database-per-service.md)): `identity_db` and
`economy_db` are separate Postgres databases, each owned outright by one service. No cross-database
foreign key exists anywhere in this system — a reference from one database to a row in the other is
always a loose id, resolved by calling the owning service's API, not by joining across a database
boundary that doesn't exist at the connection-string level.

## Who owns what

| Database | Owner | Key entities |
|---|---|---|
| `identity_db` | IdentityService | `Users`, `Games`, `UserGameRoles`, `RolePermissions`, `RefreshTokenFamilies`/`RefreshTokens`, `RevokedAccessTokens`, `EmailVerificationCodes`, `PasswordResetTokens`, `ExternalLogins` |
| `economy_db` | EconomyService | `Currencies`, `Balances`, `LedgerEntries`, `ConversionRates`, `ConversionRequests`, plus its own `OutboxMessages`/`ProcessedMessages` for the outbox/inbox pattern |

`Game` is the one entity every other table's tenancy hangs off: `identity_db` owns the `Games` table
itself, `economy_db`'s `Currencies`/`Balances` carry a `game_id` column with no foreign key back to
it — EconomyService trusts the id it's given rather than validating it against IdentityService on
every write, the same trade-off [ADR 0005](../adr/0005-multi-tenancy-gameid.md) makes for `GameId` as
a tenancy key generally.

`Platform.Worker` is the one deliberate exception to "one service, one database": its cleanup jobs
connect to both `identity_db` and `economy_db` directly, because token/session cleanup is
infrastructure housekeeping, not domain logic either service needs to expose through its own API. See
[Backend deployment topology](backend.md) and the
[README's Platform.Worker section](../../README.md#platformworker) for the job list.

## What crossing the boundary costs

Two real decisions in this system show the actual cost of not having a cross-database foreign key:

- **Welcome grant on email confirmation.** IdentityService can't insert a starting `Balance` row for
  a newly-confirmed user directly — it doesn't own `economy_db`. It publishes `UserEmailConfirmedEvent`
  onto its outbox instead; EconomyService's own consumer reacts and creates the balance. The user's
  first currency exists only after that round trip completes, not synchronously with confirmation. See
  [Messaging](../messaging.md) and [ADR 0010](../adr/0010-transactional-outbox-event-bus.md).
- **Game hard-delete leaves EconomyService data orphaned.** Deleting a `Game` row from `identity_db`
  cascades cleanly through IdentityService's own tables (`RolePermissions`, `UserGameRoles`,
  `RefreshTokenFamilies` all cascade on the foreign key), but EconomyService's currencies, balances,
  and conversion history for that `game_id` are left exactly as they were — nothing tells
  EconomyService the game is gone, because nothing tells it anything; there's no cross-database
  cascade, and no event for this case yet. See
  [ADR 0026](../adr/0026-game-hard-delete-orphaned-economy-data.md) for the full trade-off and what
  would actually close the gap (a `GameDeleted` event on the same outbox pipeline the welcome grant
  already uses).

Both are the same shape of problem: cross-service consistency here is eventual and event-driven where
it's been built at all, and absent where it hasn't. Neither is a bug — it's the direct, named
consequence of choosing database-per-service in [ADR 0001](../adr/0001-database-per-service.md) over
a shared database, which would have made both of these trivial at the cost of the isolation that
decision was made for.

## Migrations

Each service owns its own EF Core migrations against its own database — there's no shared migration
project and no scenario where one service's migration touches the other's schema. `identity-migrator`
and `economy-migrator` are separate one-shot containers (compose) / Kubernetes Jobs, run ahead of
their respective service's own container starting. See
[Backend deployment topology](backend.md) for how that ordering is enforced in both environments.

## Related documentation

- [ADR 0001: Database per service](../adr/0001-database-per-service.md)
- [ADR 0005: Multi-tenancy via GameId](../adr/0005-multi-tenancy-gameid.md)
- [ADR 0010: Transactional outbox](../adr/0010-transactional-outbox-event-bus.md)
- [ADR 0026: Game hard-delete](../adr/0026-game-hard-delete-orphaned-economy-data.md)
- [Messaging](../messaging.md)
