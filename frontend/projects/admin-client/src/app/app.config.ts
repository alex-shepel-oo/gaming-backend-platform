import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
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
    { provide: CLIENT_TYPE, useValue: 'admin' },
    { provide: GUEST_REDIRECT_PATH, useValue: '/dashboard' },
    // otel-collector's OTLP/HTTP port, exposed straight to the host like every other observability
    // container in infra/docker-compose.yml -- same dev-only-literal posture as that file.
    provideFrontendTelemetry({ appName: 'admin-client', otlpEndpoint: 'http://localhost:4318' }),
  ],
};
