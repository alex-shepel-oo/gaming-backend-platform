import { HttpClient, provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { TokenStore } from '../auth/token-store';
import { provideFrontendTelemetry } from './frontend-telemetry.provider';

describe('provideFrontendTelemetry', () => {
  it('registers without throwing, then tags a post-login request with a traceparent header', () => {
    const originalFetch = globalThis.fetch;
    // HttpClient's default backend in this Angular version is FetchBackend, which reads
    // globalThis.fetch fresh on every call -- installing the spy before registering telemetry
    // means Faro's fetch instrumentation wraps *this* spy as its "original", so by the time the
    // spy is invoked the wrapper has already injected the traceparent header into the request
    // options it forwards on, exactly what a real network call would receive.
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

      TestBed.inject(HttpClient)
        .get('/telemetry-test-endpoint')
        .subscribe({ error: () => undefined });

      expect(fetchSpy).toHaveBeenCalled();

      const [, requestInit] = fetchSpy.mock.calls[0];
      const headers = requestInit?.headers as Headers;
      expect(headers.get('traceparent')).toBeTruthy();
    } finally {
      globalThis.fetch = originalFetch;
    }
  });
});
