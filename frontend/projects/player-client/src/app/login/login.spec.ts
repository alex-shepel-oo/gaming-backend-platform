import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { IdentityAuthEndpoints } from 'shared';
import { Login } from './login';

describe('Login', () => {
  let httpMock: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Login],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function createAndSubmit(): ComponentFixture<Login> {
    const fixture = TestBed.createComponent(Login);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const emailInput = element.querySelector('input[type="email"]') as HTMLInputElement;
    const passwordInput = element.querySelector('input[type="password"]') as HTMLInputElement;

    emailInput.value = 'player@example.com';
    emailInput.dispatchEvent(new Event('input'));
    passwordInput.value = 'correct-password';
    passwordInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (element.querySelector('form') as HTMLFormElement).dispatchEvent(
      new Event('submit', { cancelable: true }),
    );

    return fixture;
  }

  it('redirects to Games on a successful login', () => {
    const navigateSpy = vi.spyOn(router, 'navigateByUrl');

    createAndSubmit();

    const request = httpMock.expectOne(IdentityAuthEndpoints.login);
    expect(request.request.body).toEqual({
      email: 'player@example.com',
      password: 'correct-password',
    });
    expect(request.request.body).not.toHaveProperty('gameSlug');
    request.flush({ accessToken: 'the-access-token' });

    expect(navigateSpy).toHaveBeenCalledWith('/games');
  });

  it('shows an invalid-credentials message on a 401', () => {
    const fixture = createAndSubmit();

    httpMock
      .expectOne(IdentityAuthEndpoints.login)
      .flush({ status: 401, title: 'Invalid credentials' }, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Invalid email or password.');
    expect(text).not.toContain('confirm your email');
  });

  it('shows a different, email-not-confirmed message on a 403 of that kind', () => {
    const fixture = createAndSubmit();

    httpMock.expectOne(IdentityAuthEndpoints.login).flush(
      {
        status: 403,
        title: 'Email not confirmed',
        type: 'https://gaming-backend-platform/problems/email-not-confirmed',
      },
      { status: 403, statusText: 'Forbidden' },
    );
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('confirm your email');
    expect(text).not.toContain('Invalid email or password.');
  });

  it('toggles the password field between hidden and visible', () => {
    const fixture = TestBed.createComponent(Login);
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
