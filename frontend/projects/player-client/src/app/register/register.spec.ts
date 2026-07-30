import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { IdentityAuthEndpoints } from 'shared';
import { Register } from './register';

describe('Register', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Register],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function createAndSubmit(): ComponentFixture<Register> {
    const fixture = TestBed.createComponent(Register);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const emailInput = element.querySelector('input[type="email"]') as HTMLInputElement;
    const nameInput = element.querySelector('input[type="text"]') as HTMLInputElement;
    const passwordInput = element.querySelector('input[type="password"]') as HTMLInputElement;

    emailInput.value = 'newplayer@example.com';
    emailInput.dispatchEvent(new Event('input'));
    nameInput.value = 'New Player';
    nameInput.dispatchEvent(new Event('input'));
    passwordInput.value = 'a-strong-password';
    passwordInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (element.querySelector('form') as HTMLFormElement).dispatchEvent(
      new Event('submit', { cancelable: true }),
    );

    return fixture;
  }

  it('emits the accepted response and resets the form on a successful registration', () => {
    const fixture = createAndSubmit();
    const emitSpy = vi.fn();
    fixture.componentInstance.registered.subscribe(emitSpy);

    const request = httpMock.expectOne(IdentityAuthEndpoints.register);
    expect(request.request.body).toEqual({
      email: 'newplayer@example.com',
      displayName: 'New Player',
      password: 'a-strong-password',
      gameSlug: 'demo-shooter',
    });

    request.flush(
      { userId: 'user-1', email: 'newplayer@example.com', verificationRequired: true, codeExpiresAt: '2026-07-23T00:20:00Z' },
      { status: 202, statusText: 'Accepted' },
    );

    expect(emitSpy).toHaveBeenCalledWith({
      response: {
        userId: 'user-1',
        email: 'newplayer@example.com',
        verificationRequired: true,
        codeExpiresAt: '2026-07-23T00:20:00Z',
      },
      password: 'a-strong-password',
    });
  });

  it('shows an email-taken message on a 409', () => {
    const fixture = createAndSubmit();

    httpMock
      .expectOne(IdentityAuthEndpoints.register)
      .flush({ status: 409, title: 'Email already exists' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('An account with this email already exists');
  });

  it('shows a different, rate-limited message on a 429', () => {
    const fixture = createAndSubmit();

    httpMock
      .expectOne(IdentityAuthEndpoints.register)
      .flush({ status: 429, title: 'Too many requests' }, { status: 429, statusText: 'Too Many Requests' });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Too many attempts');
    expect(text).not.toContain('already exists');
  });

  it('toggles the password field between hidden and visible', () => {
    const fixture = TestBed.createComponent(Register);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const passwordInput = element.querySelector('input[autocomplete="new-password"]') as HTMLInputElement;
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
