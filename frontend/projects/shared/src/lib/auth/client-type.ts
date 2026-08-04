import { InjectionToken } from '@angular/core';

// The one thing that varies between consuming apps: which surface an app
// identifies as on every auth call (login/select-game/refresh/logout). The
// factory default of 'web' means player-client and any spec that does not
// explicitly provide this token keep behaving exactly as before -- only an
// app that opts in (e.g. admin-client with 'admin') sees a different value.
export const CLIENT_TYPE = new InjectionToken<string>('CLIENT_TYPE', { factory: () => 'web' });
