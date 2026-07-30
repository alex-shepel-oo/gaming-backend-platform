import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { IdentityAuthEndpoints } from 'shared';
import { AdminLogin } from './admin-login';

function fakeAccessToken(scope: string): string {
  const payload = { sub: 'user-1', email: 'admin@example.com', name: 'Admin One', scope };

  return `header.${btoa(JSON.stringify(payload))}.signature`;
}

@Component({ selector: 'test-stub', template: '' })
class RouteStub {}

describe('AdminLogin', () => {
  let httpMock: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AdminLogin],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // Real route targets (not []) -- navigateByUrl to a route the test
        // router doesn't know about throws NG04002 as an unhandled
        // rejection and fails the run even though every assertion passes.
        provideRouter([
          { path: 'dashboard', component: RouteStub },
          { path: 'select-game', component: RouteStub },
        ]),
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function createAndSubmit(): ComponentFixture<AdminLogin> {
    const fixture = TestBed.createComponent(AdminLogin);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const emailInput = element.querySelector('input[type="email"]') as HTMLInputElement;
    const passwordInput = element.querySelector('input[type="password"]') as HTMLInputElement;

    emailInput.value = 'admin@example.com';
    emailInput.dispatchEvent(new Event('input'));
    passwordInput.value = 'correct-password';
    passwordInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (element.querySelector('form') as HTMLFormElement).dispatchEvent(
      new Event('submit', { cancelable: true }),
    );

    return fixture;
  }

  it('sends no gameSlug -- login is always account-first, never game-specific', () => {
    createAndSubmit();

    const request = httpMock.expectOne(IdentityAuthEndpoints.login);
    expect(request.request.body).toEqual({ email: 'admin@example.com', password: 'correct-password' });
    expect(request.request.body).not.toHaveProperty('gameSlug');
    request.flush({ accessToken: fakeAccessToken('Platform') });
  });

  it('routes a platform-scoped login straight to the dashboard, no picker', () => {
    const navigateSpy = vi.spyOn(router, 'navigateByUrl');

    createAndSubmit();

    httpMock.expectOne(IdentityAuthEndpoints.login).flush({ accessToken: fakeAccessToken('Platform') });

    expect(navigateSpy).toHaveBeenCalledWith('/dashboard');
  });

  it('routes an account-scoped login (no platform role yet) to the game picker', () => {
    const navigateSpy = vi.spyOn(router, 'navigateByUrl');

    createAndSubmit();

    httpMock.expectOne(IdentityAuthEndpoints.login).flush({ accessToken: fakeAccessToken('Account') });

    expect(navigateSpy).toHaveBeenCalledWith('/select-game');
  });

  it('treats a game-scoped login the same as platform -- straight to the dashboard', () => {
    const navigateSpy = vi.spyOn(router, 'navigateByUrl');

    createAndSubmit();

    httpMock.expectOne(IdentityAuthEndpoints.login).flush({ accessToken: fakeAccessToken('Game') });

    expect(navigateSpy).toHaveBeenCalledWith('/dashboard');
  });

  it('shows an invalid-credentials message on a 401', () => {
    const fixture = createAndSubmit();

    httpMock
      .expectOne(IdentityAuthEndpoints.login)
      .flush({ status: 401, title: 'Invalid credentials' }, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Invalid email or password.');
  });

  it('toggles the password field between hidden and visible', () => {
    const fixture = TestBed.createComponent(AdminLogin);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const passwordInput = element.querySelector('input[autocomplete="current-password"]') as HTMLInputElement;
    const toggleButton = element.querySelector('button[aria-label="Show password"]') as HTMLButtonElement;

    expect(passwordInput.type).toBe('password');

    toggleButton.click();
    fixture.detectChanges();

    expect(passwordInput.type).toBe('text');
    expect(element.querySelector('button[aria-label="Hide password"]')).toBe(toggleButton);

    toggleButton.click();
    fixture.detectChanges();

    expect(passwordInput.type).toBe('password');
    expect(element.querySelector('button[aria-label="Show password"]')).toBe(toggleButton);
  });
});
