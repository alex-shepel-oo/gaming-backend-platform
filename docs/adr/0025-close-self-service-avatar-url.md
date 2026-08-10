# ADR-0025: Close self-service avatar URLs

- **Status:** Accepted
- **Date:** 2026-08-10

## Context

`player-client`'s Profile screen let any registered player set an arbitrary
`avatarUrl` on their own account (`PATCH /api/identity/users/me`), rendered
directly as an `<img src>` on their own profile and anywhere else the app
shows an avatar. The only server-side check was
`UrlValidation.TryNormalize` — an absolute-URL parse restricted to the
`http`/`https` schemes, the same helper already used for admin-only game
`iconUrl` updates. It rules out `javascript:`/`data:`-scheme abuse, but
nothing stops a player from pointing it at hotlinked, offensive, or
tracking-pixel content, and there's no image proxy, upload pipeline, or
moderation queue in this stack to do better than a scheme check.

Unlike game `iconUrl` (admin-only, a small trusted population), avatar URLs
were reachable by every self-registering player — the largest, least trusted
caller population the platform has.

## Decision

**Remove the write path entirely rather than build real validation for it.**
`UpdateProfileRequest` (`IdentityService/Contracts/Requests/`) no longer has
an `AvatarUrl` property at all — a client sending one has it silently
ignored, not rejected. `InvalidAvatarUrlException` and its
`GlobalExceptionHandler` mapping were deleted alongside it, since nothing
throws it anymore. `player-client`'s Profile edit form drops the "Avatar
URL" field; `ProfileService.updateMe()`'s TypeScript request type drops the
field to match, so a future caller can't silently reintroduce it from the
client side either.

**The read side is untouched.** `GET /users/me` still returns `avatarUrl`,
and a previously-set value still renders exactly as before — this closes the
write surface, it doesn't retroactively wipe or reinterpret existing data.
`UrlValidation` itself is untouched and still backs game `iconUrl` updates,
which stay admin-only and out of scope for this decision.

## Alternatives considered

| Alternative | Why it was not chosen |
|---|---|
| Keep the field, add stricter validation (allowlisted image hosts, HEAD-request content-type check, size limit) | Real effort for a demo-scale project's player-facing convenience field; still doesn't solve hotlinking/tracking/offensive-content risk, only narrows the URL-scheme risk further |
| Proxy avatar URLs through a server-side image fetch/resize step | The actual right long-term answer if avatars come back, but a real new piece of infrastructure (needs the S3/Garage storage on the roadmap first) — not something to build as a side effect of closing a gap |
| Restrict to admin-settable avatars only, keep the endpoint | No admin UI or use case ever existed for setting *another user's* avatar; would have added a feature nobody asked for while still not solving the original self-service risk |
| Leave it open, accept the risk | The population able to reach it (every self-registering player) made this the platform's widest-reach unvalidated external-URL input; not proportionate to leave open for a field with no real product requirement behind it |

## Consequences

### Benefits

The platform's single largest unmoderated external-URL input, reachable by
its least-trusted caller population, no longer exists. No new
infrastructure, migration, or validation logic was needed — removing a
write path is strictly simpler than securing one.

### Trade-offs

Players who already had a custom avatar keep seeing it; anyone who wants a
new one can't set it anymore, with no in-app explanation beyond the field's
absence. There's no admin override to set one on a player's behalf either —
if a real product need for player avatars resurfaces, it needs the proper
upload/moderation path below, not a re-opened URL field.

### When to revisit

If avatar customization becomes a real product requirement again, revisit
once there's a real place to store uploaded images — the S3-compatible
Garage instance on the landing page's roadmap is the natural target — behind
an actual upload endpoint (size/type validated, re-encoded server-side) 
rather than reopening arbitrary-URL input.
