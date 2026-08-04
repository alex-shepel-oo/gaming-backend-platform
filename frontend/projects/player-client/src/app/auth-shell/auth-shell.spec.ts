import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { IdentityAuthEndpoints } from 'shared';
import { AuthShell } from './auth-shell';

describe('AuthShell', () => {
  let httpMock: HttpTestingController;
  let router: Router;
  let fixture: ComponentFixture<AuthShell>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AuthShell],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    fixture = TestBed.createComponent(AuthShell);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const registerTabLabel = Array.from(element.querySelectorAll<HTMLElement>('[role="tab"]')).find((tab) =>
      tab.textContent?.includes('Register'),
    )!;
    registerTabLabel.click();
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
  });

  function registerAndFlush(verificationRequired: boolean): void {
    const element = fixture.nativeElement as HTMLElement;
    const emailInput = element.querySelectorAll('input[type="email"]')[0] as HTMLInputElement;
    const nameInput = element.querySelector('input[type="text"]') as HTMLInputElement;
    const passwordInput = element.querySelectorAll('input[type="password"]')[0] as HTMLInputElement;

    emailInput.value = 'newplayer@example.com';
    emailInput.dispatchEvent(new Event('input'));
    nameInput.value = 'New Player';
    nameInput.dispatchEvent(new Event('input'));
    passwordInput.value = 'a-strong-password';
    passwordInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const registerForm = Array.from(element.querySelectorAll('form')).find((form) =>
      form.querySelector('input[type="text"]'),
    )!;
    registerForm.dispatchEvent(new Event('submit', { cancelable: true }));

    httpMock.expectOne(IdentityAuthEndpoints.register).flush(
      {
        userId: 'user-1',
        email: 'newplayer@example.com',
        verificationRequired,
        codeExpiresAt: verificationRequired ? '2026-07-23T00:20:00Z' : null,
      },
      { status: 202, statusText: 'Accepted' },
    );
    fixture.detectChanges();
  }

  it('switches to the code-entry step after a registration that needs verification', () => {
    registerAndFlush(true);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('newplayer@example.com');
    expect(text).toContain('Verification code');
  });

  it('logs the user straight in and redirects to /games after confirming the code', () => {
    const navigateSpy = vi.spyOn(router, 'navigateByUrl');
    registerAndFlush(true);

    const element = fixture.nativeElement as HTMLElement;
    const codeInput = element.querySelector('input[inputmode="numeric"]') as HTMLInputElement;
    codeInput.value = '123456';
    codeInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    httpMock.expectOne(IdentityAuthEndpoints.confirmEmail).flush(null);

    const loginRequest = httpMock.expectOne(IdentityAuthEndpoints.login);
    expect(loginRequest.request.body).toEqual({ email: 'newplayer@example.com', password: 'a-strong-password' });
    loginRequest.flush({ accessToken: 'the-access-token' });

    expect(navigateSpy).toHaveBeenCalledWith('/games');
  });

  it('falls back to the Login tab with a success notice if the auto-login fails', () => {
    registerAndFlush(true);

    const element = fixture.nativeElement as HTMLElement;
    const codeInput = element.querySelector('input[inputmode="numeric"]') as HTMLInputElement;
    codeInput.value = '123456';
    codeInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    httpMock.expectOne(IdentityAuthEndpoints.confirmEmail).flush(null);
    httpMock
      .expectOne(IdentityAuthEndpoints.login)
      .flush({ status: 401, title: 'Invalid credentials' }, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();

    const text = element.textContent ?? '';
    expect(text).toContain('Email confirmed');
    expect(text).toContain('Log in');
  });

  it('shows a success notice without a code step when verification is not required', () => {
    registerAndFlush(false);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Registration complete');
    expect(text).not.toContain('Verification code');
  });
});
