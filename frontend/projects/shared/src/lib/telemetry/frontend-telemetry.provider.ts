import { EnvironmentProviders, effect, inject, Injector, provideAppInitializer } from '@angular/core';
import { TokenStore } from '../auth/token-store';

export interface FrontendTelemetryOptions {
  /** Shows up as the span's service name in Tempo/Grafana, mirrors AddPlatformTelemetry's serviceName. */
  appName: string;
  /** The same otel-collector every backend service exports to, e.g. http://localhost:4318. Base URL, no path. */
  otlpEndpoint: string;
}

/**
 * Frontend counterpart to BuildingBlocks.Telemetry's AddPlatformTelemetry: wires Grafana Faro's
 * tracing instrumentation so browser-originated fetch/XHR calls carry a `traceparent` header,
 * continuing the same trace the backend starts, and exports spans via OTLP/HTTP straight to
 * otel-collector; there's no separate Faro-specific receiver anywhere in this stack.
 *
 * The actual Faro/OpenTelemetry setup lives in frontend-telemetry.init.ts and is reached only via
 * a dynamic `import()` below, kicked off but deliberately not awaited by the app initializer. The
 * app becomes interactive without waiting on it, and the heavy CJS packages it pulls in land in
 * their own lazy chunk instead of every visitor's initial payload. See that file's doc comment for
 * why the packages need splitting out at all.
 */
export function provideFrontendTelemetry(options: FrontendTelemetryOptions): EnvironmentProviders {
  return provideAppInitializer(() => {
    const tokenStore = inject(TokenStore);
    const injector = inject(Injector);

    void import('./frontend-telemetry.init')
      .then(({ initFrontendTelemetry }) => {
        const enduserProcessor = initFrontendTelemetry(options);

        effect(() => enduserProcessor.setUserId(tokenStore.claims()?.userId ?? null), { injector });
      })
      // Never awaited/rethrown by the app initializer above, so a failure here (e.g. the chunk
      // fails to load) would otherwise surface only as a silent, unobservable rejection.
      .catch((error: unknown) => console.error('Failed to initialize frontend telemetry', error));
  });
}
