import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { authInterceptor, CLIENT_TYPE, GUEST_REDIRECT_PATH, provideSilentSessionRestore } from 'shared';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withInterceptors([authInterceptor])),
    provideSilentSessionRestore(),
    provideRouter(routes),
    { provide: CLIENT_TYPE, useValue: 'admin' },
    { provide: GUEST_REDIRECT_PATH, useValue: '/dashboard' },
  ],
};
