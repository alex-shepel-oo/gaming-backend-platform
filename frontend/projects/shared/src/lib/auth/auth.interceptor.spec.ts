import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { authInterceptor } from './auth.interceptor';
import { CLIENT_TYPE } from './client-type';
import { IdentityAuthEndpoints } from './identity-auth-endpoints';
import { TokenStore } from './token-store';

const protectedUrl = '/api/economy/balances/me';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let tokenStore: TokenStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    tokenStore = TestBed.inject(TokenStore);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('attaches the access token from the token store to outgoing requests', () => {
    tokenStore.set('the-access-token');

    http.get(protectedUrl).subscribe();

    const request = httpMock.expectOne(protectedUrl);
    expect(request.request.headers.get('Authorization')).toBe('Bearer the-access-token');
    expect(request.request.withCredentials).toBe(true);

    request.flush({});
  });

  it('on a 401 refreshes once and retries the original request exactly once', () => {
    tokenStore.set('expired-token');

    let result: unknown;
    http.get(protectedUrl).subscribe({ next: (value) => (result = value) });

    httpMock.expectOne(protectedUrl).flush(null, { status: 401, statusText: 'Unauthorized' });

    const refreshRequest = httpMock.expectOne(IdentityAuthEndpoints.refresh);
    refreshRequest.flush({ accessToken: 'new-access-token' });

    const retryRequest = httpMock.expectOne(protectedUrl);
    expect(retryRequest.request.headers.get('Authorization')).toBe('Bearer new-access-token');
    retryRequest.flush({ balance: 42 });

    expect(result).toEqual({ balance: 42 });
    expect(tokenStore.read()).toBe('new-access-token');
  });

  it('collapses two concurrent 401s into a single refresh call', () => {
    const otherProtectedUrl = '/api/economy/transactions/me';
    tokenStore.set('expired-token');

    let resultA: unknown;
    let resultB: unknown;
    http.get(protectedUrl).subscribe({ next: (value) => (resultA = value) });
    http.get(otherProtectedUrl).subscribe({ next: (value) => (resultB = value) });

    httpMock.expectOne(protectedUrl).flush(null, { status: 401, statusText: 'Unauthorized' });
    httpMock.expectOne(otherProtectedUrl).flush(null, { status: 401, statusText: 'Unauthorized' });

    // httpMock.expectOne throws if more than one refresh request went out --
    // this is the actual regression check: two concurrent 401s must share
    // one refresh call, not race two against the same refresh cookie.
    const refreshRequest = httpMock.expectOne(IdentityAuthEndpoints.refresh);
    refreshRequest.flush({ accessToken: 'new-access-token' });

    httpMock.expectOne(protectedUrl).flush({ from: 'a' });
    httpMock.expectOne(otherProtectedUrl).flush({ from: 'b' });

    expect(resultA).toEqual({ from: 'a' });
    expect(resultB).toEqual({ from: 'b' });
  });

  it('does not loop when the refresh itself also fails, and clears the session', () => {
    tokenStore.set('expired-token');

    let error: unknown;
    http.get(protectedUrl).subscribe({ error: (e) => (error = e) });

    httpMock.expectOne(protectedUrl).flush(null, { status: 401, statusText: 'Unauthorized' });

    // Refresh fails too (expired/reused cookie): must not trigger another refresh attempt.
    httpMock.expectOne(IdentityAuthEndpoints.refresh).flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect((error as HttpErrorResponse).status).toBe(401);
    expect(tokenStore.read()).toBeNull();
    // httpMock.verify() in afterEach fails the test if any further refresh/retry request was made.
  });

  it('sends the injected CLIENT_TYPE, not a hardcoded value, on the refresh-retry call', () => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: CLIENT_TYPE, useValue: 'admin' },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    tokenStore = TestBed.inject(TokenStore);

    tokenStore.set('expired-token');

    http.get(protectedUrl).subscribe();

    httpMock.expectOne(protectedUrl).flush(null, { status: 401, statusText: 'Unauthorized' });

    const refreshRequest = httpMock.expectOne(IdentityAuthEndpoints.refresh);
    expect(refreshRequest.request.headers.get('X-Client-Type')).toBe('admin');
    refreshRequest.flush({ accessToken: 'new-access-token' });

    httpMock.expectOne(protectedUrl).flush({});
  });

  it('never writes the access token to the console', () => {
    const consoleLogSpy = vi.spyOn(console, 'log').mockImplementation(() => {});

    tokenStore.set('super-secret-token');

    http.get(protectedUrl).subscribe({ error: () => {} });

    httpMock.expectOne(protectedUrl).flush(null, { status: 401, statusText: 'Unauthorized' });
    httpMock.expectOne(IdentityAuthEndpoints.refresh).flush({ accessToken: 'rotated-secret-token' });
    httpMock.expectOne(protectedUrl).flush({});

    for (const call of consoleLogSpy.mock.calls) {
      const serialized = call.map((arg) => JSON.stringify(arg)).join(' ');
      expect(serialized).not.toContain('super-secret-token');
      expect(serialized).not.toContain('rotated-secret-token');
    }

    consoleLogSpy.mockRestore();
  });
});
