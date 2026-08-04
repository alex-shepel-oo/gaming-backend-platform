import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { authInterceptor, provideFrontendTelemetry, provideSilentSessionRestore } from 'shared';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withInterceptors([authInterceptor])),
    provideSilentSessionRestore(),
    provideRouter(routes),
    // Relative path, proxied same-origin to otel-collector by nginx.conf's /otlp/ location --
    // mirrors how /api and /hubs already reach their backends, so the browser never talks to
    // otel-collector's own origin directly.
    provideFrontendTelemetry({ appName: 'player-client', otlpEndpoint: '/otlp' }),
  ],
};
