import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';
import { IdentityAuthEndpoints } from './identity-auth-endpoints';
import { restoreSessionSilently } from './silent-session-restore';
import { TokenStore } from './token-store';

describe('restoreSessionSilently', () => {
  let httpMock: HttpTestingController;
  let authService: AuthService;
  let tokenStore: TokenStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    httpMock = TestBed.inject(HttpTestingController);
    authService = TestBed.inject(AuthService);
    tokenStore = TestBed.inject(TokenStore);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('restores the access token from the refresh cookie on a successful reload', async () => {
    const restored = restoreSessionSilently(authService);

    httpMock.expectOne(IdentityAuthEndpoints.refresh).flush({ accessToken: 'restored-access-token' });

    await restored;

    expect(tokenStore.read()).toBe('restored-access-token');
  });

  it('leaves the token store empty and resolves without throwing when there is no valid session', async () => {
    const restored = restoreSessionSilently(authService);

    httpMock.expectOne(IdentityAuthEndpoints.refresh).flush(null, { status: 401, statusText: 'Unauthorized' });

    await expect(restored).resolves.toBeUndefined();
    expect(tokenStore.read()).toBeNull();
  });
});
