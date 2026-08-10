import { HttpClient, provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { TokenStore } from '../auth/token-store';
import { provideFrontendTelemetry } from './frontend-telemetry.provider';

describe('provideFrontendTelemetry', () => {
  it('registers without throwing, then tags a post-login request with a traceparent header once the lazy telemetry chunk loads', async () => {
    const originalFetch = globalThis.fetch;
    // HttpClient's default backend in this Angular version is FetchBackend, which reads
    // globalThis.fetch fresh on every call. Installing the spy before registering telemetry
    // means Faro's fetch instrumentation wraps *this* spy as its "original", so by the time
    // the spy is invoked the wrapper has already injected the traceparent header into the
    // request options it forwards on, exactly what a real network call would receive.
    const fetchSpy = vi.fn((_input: RequestInfo | URL, _init?: RequestInit) =>
      Promise.reject(new Error('no real network in this test')),
    );
    globalThis.fetch = fetchSpy as unknown as typeof fetch;

    try {
      expect(() =>
        TestBed.configureTestingModule({
          providers: [
            provideFrontendTelemetry({ appName: 'test-app', otlpEndpoint: 'http://localhost:4318' }),
            provideHttpClient(),
          ],
        }),
      ).not.toThrow();

      TestBed.inject(TokenStore).set('a-valid-access-token');

      // The provider kicks off the telemetry chunk's dynamic import without awaiting it, since
      // the whole point is that the app never blocks on it, so this polls rather than asserting
      // immediately, standing in for however long that chunk takes to load in a real browser.
      await vi.waitFor(
        () => {
          TestBed.inject(HttpClient)
            .get('/telemetry-test-endpoint')
            .subscribe({ error: () => undefined });

          expect(fetchSpy).toHaveBeenCalled();

          const [, requestInit] = fetchSpy.mock.calls.at(-1)!;
          const headers = requestInit?.headers as Headers;
          expect(headers?.get?.('traceparent')).toBeTruthy();
        },
        // Unlike the rest of this module's imports, the telemetry chunk is a genuinely separate
        // dynamic import here: Vitest has to transform it (and its @opentelemetry/@grafana-faro
        // dependency tree) on first hit rather than reusing an already-warm module graph, which
        // comfortably outlasts vi.waitFor's default 1s timeout.
        { timeout: 10_000 },
      );
    } finally {
      globalThis.fetch = originalFetch;
    }
  }, 15_000); // vi.waitFor's own 10s allowance (see above) needs a longer test timeout than vitest's 5s default to actually matter.
});
