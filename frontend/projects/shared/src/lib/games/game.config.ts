// MVP: player-client serves a single game deployment, so the slug is a
// build-time constant rather than something the player picks. Multi-game
// login would need either an anonymous games listing or a post-login
// game-switch flow -- neither exists yet (see README known limitations).
export const DEFAULT_GAME_SLUG = 'demo-shooter';
