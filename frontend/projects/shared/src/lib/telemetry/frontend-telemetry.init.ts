import { initializeFaro, SessionInstrumentation } from '@grafana/faro-web-sdk';
import { getDefaultOTELInstrumentations, TracingInstrumentation } from '@grafana/faro-web-tracing';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http';
import { BatchSpanProcessor } from '@opentelemetry/sdk-trace-web';
import { EnduserIdSpanProcessor } from './enduser-id-span-processor';
import type { FrontendTelemetryOptions } from './frontend-telemetry.provider';

/**
 * Split out of frontend-telemetry.provider.ts on purpose: `@grafana/faro-web-sdk`,
 * `@grafana/faro-web-tracing` and the `@opentelemetry/*` packages are CommonJS, which defeats
 * esbuild's tree-shaking and pulls their full weight into whatever chunk imports them. Kept out of
 * the provider's own (eager, main-bundle) imports and only reached via a dynamic `import()`, this
 * becomes its own lazy chunk that loads in the background after bootstrap instead of inflating the
 * initial payload every visitor pays for on first paint.
 */
export function initFrontendTelemetry(options: FrontendTelemetryOptions): EnduserIdSpanProcessor {
  const tracesUrl = `${options.otlpEndpoint}/v1/traces`;
  const enduserProcessor = new EnduserIdSpanProcessor(
    new BatchSpanProcessor(new OTLPTraceExporter({ url: tracesUrl })),
  );

  initializeFaro({
    app: { name: options.appName },
    // No Faro-format signal (errors, logs, Web Vitals) is shipped anywhere yet -- see
    // frontend-telemetry.provider.ts's doc comment -- so there's no transport to hand Faro. An
    // empty array (rather than leaving this unset) keeps Faro from logging a warning about a
    // missing url/transports for a gap that's deliberate, not an oversight.
    transports: [],
    instrumentations: [
      new SessionInstrumentation(),
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

  return enduserProcessor;
}
