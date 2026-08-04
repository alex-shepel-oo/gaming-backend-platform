import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';
import { CLIENT_TYPE } from './client-type';
import { IdentityAuthEndpoints } from './identity-auth-endpoints';

describe('AuthService', () => {
  let httpMock: HttpTestingController;

  afterEach(() => {
    httpMock.verify();
  });

  it('sends X-Client-Type: web when no app overrides CLIENT_TYPE, on every auth call', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    const service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);

    service.login({ email: 'a@b.com', password: 'pw' }).subscribe();
    const loginRequest = httpMock.expectOne(IdentityAuthEndpoints.login);
    expect(loginRequest.request.headers.get('X-Client-Type')).toBe('web');
    loginRequest.flush({ accessToken: 'token' });

    service.selectGame('game-1').subscribe();
    const selectGameRequest = httpMock.expectOne(IdentityAuthEndpoints.selectGame);
    expect(selectGameRequest.request.headers.get('X-Client-Type')).toBe('web');
    selectGameRequest.flush({ accessToken: 'token' });

    service.refresh().subscribe();
    const refreshRequest = httpMock.expectOne(IdentityAuthEndpoints.refresh);
    expect(refreshRequest.request.headers.get('X-Client-Type')).toBe('web');
    refreshRequest.flush({ accessToken: 'token' });

    service.logout().subscribe();
    const logoutRequest = httpMock.expectOne(IdentityAuthEndpoints.logout);
    expect(logoutRequest.request.headers.get('X-Client-Type')).toBe('web');
    logoutRequest.flush(null);
  });

  it('sends the app-provided CLIENT_TYPE instead, when one is set', () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: CLIENT_TYPE, useValue: 'admin' },
      ],
    });

    const service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);

    service.login({ email: 'a@b.com', password: 'pw' }).subscribe();

    const request = httpMock.expectOne(IdentityAuthEndpoints.login);
    expect(request.request.headers.get('X-Client-Type')).toBe('admin');
    request.flush({ accessToken: 'token' });
  });
});
