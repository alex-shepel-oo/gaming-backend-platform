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
    // otel-collector's OTLP/HTTP port, exposed straight to the host like every other observability
    // container in infra/docker-compose.yml -- same dev-only-literal posture as that file.
    provideFrontendTelemetry({ appName: 'player-client', otlpEndpoint: 'http://localhost:4318' }),
  ],
};
