import { EnvironmentProviders, effect, inject, Injector, provideAppInitializer } from '@angular/core';
import { initializeFaro } from '@grafana/faro-web-sdk';
import { getDefaultOTELInstrumentations, TracingInstrumentation } from '@grafana/faro-web-tracing';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http';
import { BatchSpanProcessor } from '@opentelemetry/sdk-trace-web';
import { TokenStore } from '../auth/token-store';
import { EnduserIdSpanProcessor } from './enduser-id-span-processor';

export interface FrontendTelemetryOptions {
  /** Shows up as the span's service name in Tempo/Grafana -- mirrors AddPlatformTelemetry's serviceName. */
  appName: string;
  /** The same otel-collector every backend service exports to, e.g. http://localhost:4318 -- base URL, no path. */
  otlpEndpoint: string;
}

/**
 * Frontend counterpart to BuildingBlocks.Telemetry's AddPlatformTelemetry: wires Grafana Faro's
 * tracing instrumentation so browser-originated fetch/XHR calls carry a `traceparent` header,
 * continuing the same trace the backend starts, and exports spans via OTLP/HTTP straight to
 * otel-collector -- there's no separate Faro-specific receiver anywhere in this stack.
 *
 * Faro's own error/log/Web-Vitals capture (`getWebInstrumentations()`) is deliberately left out.
 * That data ships in Faro's own event format, which needs a Faro receiver (e.g. Grafana Alloy's
 * `faro.receiver` component) to land anywhere useful -- standing that up is a separate
 * infrastructure decision, not something that should fall out of wiring up tracing, so only
 * TracingInstrumentation is registered below.
 */
export function provideFrontendTelemetry(options: FrontendTelemetryOptions): EnvironmentProviders {
  const tracesUrl = `${options.otlpEndpoint}/v1/traces`;
  const enduserProcessor = new EnduserIdSpanProcessor(
    new BatchSpanProcessor(new OTLPTraceExporter({ url: tracesUrl })),
  );

  initializeFaro({
    app: { name: options.appName },
    // No Faro-format signal (errors, logs, Web Vitals) is shipped anywhere yet -- see the doc
    // comment above -- so there's no transport to hand Faro. An empty array (rather than leaving
    // this unset) keeps Faro from logging a warning about a missing url/transports for a gap
    // that's deliberate, not an oversight.
    transports: [],
    instrumentations: [
      new TracingInstrumentation({
        spanProcessor: enduserProcessor,
        // The default instrumentations would otherwise also trace the exporter's own calls to
        // otel-collector, feeding spans about sending spans back into itself. ignoreUrls excludes
        // that one URL, the same way Faro already excludes its own collector URL internally when a
        // "url" config is given for its default transport.
        instrumentations: getDefaultOTELInstrumentations({ ignoreUrls: [tracesUrl] }),
      }),
    ],
  });

  return provideAppInitializer(() => {
    const tokenStore = inject(TokenStore);
    const injector = inject(Injector);

    effect(() => enduserProcessor.setUserId(tokenStore.claims()?.userId ?? null), { injector });
  });
}
