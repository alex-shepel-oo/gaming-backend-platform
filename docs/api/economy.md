# Economy API

Reachable directly at `http://localhost:5001`, and also proxied through the
gateway at `http://localhost:5100/api/economy/...` (same paths, `/api/economy`
prefix) for `balances/me`, `transactions/me`, and `conversions` — the routes
player-client actually calls. `currencies`, `balances/{userId}/adjust`,
`transactions/grant`, and `transactions/spend` stay direct-only for now; no
current client goes through the gateway for them.

Currencies come in two scopes: **platform** currencies (`gameId` is `null`,
shared across every game) and **game** currencies (`gameId` set, scoped to one
title). The seeded development data has `PLATFORM_CREDITS` (platform),
`SHOOTER_GOLD` (game, `demo-shooter`), and `RACER_TOKENS` (game, `demo-racer`),
with conversion rates of `100:1` and `40:1` respectively from platform credits.
`CurrencyDto` also carries `decimals` (default `2`), so clients know how many
fractional digits to render for a given currency without hardcoding it.

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/currencies` | bearer | Platform currencies plus the caller's own game currency |
| GET | `/balances/me` | bearer | Current user's balances — the welcome grant (see [Messaging](../messaging.md)) arrives asynchronously, not as a side effect of this call, so a balance may briefly be absent right after registration (`?gameId=` cross-checks against the token's own game, it does not select a different one) |
| POST | `/balances/{userId}/adjust` | bearer, `platform.balance.adjust` or `game.balance.adjust` (own game only) | Manual correction with a required audit `reason`; `Amount` is a signed delta, not a magnitude |
| POST | `/transactions/grant` | bearer, `platform.balance.adjust` or `game.balance.adjust` (own game only) | Credit a user's balance, with an audit `reason` |
| POST | `/transactions/spend` | bearer | Debit the caller's own balance |
| GET | `/transactions/me` | bearer | Paginated ledger history (`?currencyId=&page=&pageSize=`) for the caller only |
| POST | `/conversions` | bearer | Start a platform-to-game currency conversion; `202` with `Started`, not the final outcome |
| GET | `/conversions/{id}` | bearer | Poll a conversion's status; owner-scoped, `404` on someone else's id |
| GET | `/conversions/rate` | bearer | `?fromCurrencyId=&toCurrencyId=` - the raw rate for a pair, no side effects; `400` on an unsupported pair (same cause `POST /conversions` itself rejects with `400`) |
| POST | `/conversions/{id}/cancel` | bearer | Owner-scoped like `GET /conversions/{id}`: `404` on someone else's or a missing id; `200` with the resulting status on success; `409` once the conversion is already terminal or compensating |
| GET | `/health` | anonymous | Liveness probe |
| GET | `/health/ready` | anonymous | Readiness probe (Postgres and RabbitMQ) |

`grant`, `spend`, and `adjust` all require an `Idempotency-Key` header (400
without one); replaying the same key returns the original outcome instead of
posting twice, keyed off `ledger_entries.idempotency_key`.

## Why 402 for insufficient funds

A `spend` (or a downward `adjust`) that would take a balance below zero
returns `402 Payment Required` rather than `400`/`409`/`422`. It is the one
status code in the 4xx range whose name actually describes "not enough
money," which makes it easier to branch on client-side than yet another
generic conflict/validation code sharing space with unrelated failures.

## Why NUnit here

EconomyService's tests run on NUnit instead of xUnit (which IdentityService
uses), on purpose, to show working knowledge of both. NSubstitute,
AwesomeAssertions, and Testcontainers are the same across both projects
either way.

## Conversion saga

Converting platform currency to a game's own currency is a two-step
operation with a compensating rollback: `POST /conversions` returns `202`
with status `Started` right away, and the client polls `GET
/conversions/{id}` for the outcome. A background runner picks the request up
off an in-process channel, debits the platform balance, then credits the
game balance; if the credit step fails, a compensating entry restores the
debited amount and the request lands in `Failed` instead of `Completed`.

This is an in-process, sequential saga, not choreography over RabbitMQ -
both currencies belong to EconomyService, so there is no second service to
react to an event between the two steps. Each transition commits and is
recorded on `conversion_requests.status`, so a crash mid-saga leaves a
readable state rather than an ambiguous one. See [ADR 0010's
addendum](../adr/0010-transactional-outbox-event-bus.md#addendum-the-conversion-saga)
for the full reasoning, including why this isn't genuine cross-service
choreography (that needs InventoryService, which doesn't exist until slice
3).

Every status transition is now a compare-and-swap - it only applies if the
row's current status still matches what the writer expects, and the writer
bails out otherwise. The runner was the only thing moving a conversion's
status for a while, so nothing enforced this; `POST /conversions/{id}/cancel`
added a second writer racing the same row, and without the guard a cancel
could stamp `Failed` over a debit the runner had already posted, or the
runner could clobber a cancel that had just compensated it - either way,
money debited but never accounted for. Cancelling reacts to whichever status
it actually finds: `Started` fails the conversion outright with nothing to
reverse; `DebitDone` drives the same compensation path the runner itself uses
on a failed credit, so there's only one place that logic lives; anything
already `Completed`, `Failed`, or `Compensating` is rejected with `409`.
