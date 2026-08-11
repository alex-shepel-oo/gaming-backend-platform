import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { MAT_ICON_DEFAULT_OPTIONS } from '@angular/material/icon';
import { provideRouter } from '@angular/router';
import {
  authInterceptor,
  CLIENT_TYPE,
  GUEST_REDIRECT_PATH,
  provideFrontendTelemetry,
  provideSilentSessionRestore,
} from 'shared';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withInterceptors([authInterceptor])),
    provideSilentSessionRestore(),
    provideRouter(routes),
    { provide: MAT_ICON_DEFAULT_OPTIONS, useValue: { fontSet: 'material-symbols-outlined' } },
    { provide: CLIENT_TYPE, useValue: 'admin' },
    { provide: GUEST_REDIRECT_PATH, useValue: '/users' },
    // Relative path, proxied same-origin to otel-collector by nginx.conf's /otlp/ location --
    // mirrors how /api already reaches its backend, so the browser never talks to
    // otel-collector's own origin directly.
    provideFrontendTelemetry({ appName: 'admin-client', otlpEndpoint: '/otlp' }),
  ],
};
