import { InjectionToken } from '@angular/core';

// Same shape as CLIENT_TYPE: the one thing that varies between consuming
// apps here is where an already-authenticated visitor should land when they
// hit a guest-only route (e.g. /login). The factory default of '/games'
// means player-client and any spec that does not explicitly provide this
// token keep behaving exactly as before; only an app that opts in (e.g.
// admin-client with '/users') sees a different redirect.
export const GUEST_REDIRECT_PATH = new InjectionToken<string>('GUEST_REDIRECT_PATH', { factory: () => '/games' });
