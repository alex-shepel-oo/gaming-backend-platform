import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { IdentityAuthEndpoints } from 'shared';
import { ConfirmEmail } from './confirm-email';

describe('ConfirmEmail', () => {
  let httpMock: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    vi.useFakeTimers();

    TestBed.configureTestingModule({
      imports: [ConfirmEmail],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  function createAndSubmit(code: string): ComponentFixture<ConfirmEmail> {
    const fixture = TestBed.createComponent(ConfirmEmail);
    fixture.componentRef.setInput('email', 'newplayer@example.com');
    fixture.componentRef.setInput('password', 'a-strong-password');
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const digitInputs = Array.from(element.querySelectorAll('.code-digit')) as HTMLInputElement[];
    code.split('').forEach((digit, index) => {
      digitInputs[index].value = digit;
      digitInputs[index].dispatchEvent(new Event('input'));
    });
    fixture.detectChanges();

    (element.querySelector('form') as HTMLFormElement).dispatchEvent(
      new Event('submit', { cancelable: true }),
    );

    return fixture;
  }

  it('logs in and redirects to /games after a successful confirmation', () => {
    const navigateSpy = vi.spyOn(router, 'navigateByUrl');
    createAndSubmit('123456');

    const confirmRequest = httpMock.expectOne(IdentityAuthEndpoints.confirmEmail);
    expect(confirmRequest.request.body).toEqual({ email: 'newplayer@example.com', code: '123456' });
    confirmRequest.flush(null);

    const loginRequest = httpMock.expectOne(IdentityAuthEndpoints.login);
    expect(loginRequest.request.body).toEqual({ email: 'newplayer@example.com', password: 'a-strong-password' });
    loginRequest.flush({ accessToken: 'the-access-token' });

    expect(navigateSpy).toHaveBeenCalledWith('/games');
  });

  it('falls back to emitting confirmed if the auto-login itself fails', () => {
    const fixture = createAndSubmit('123456');
    const confirmedSpy = vi.fn();
    fixture.componentInstance.confirmed.subscribe(confirmedSpy);

    httpMock.expectOne(IdentityAuthEndpoints.confirmEmail).flush(null);
    httpMock
      .expectOne(IdentityAuthEndpoints.login)
      .flush({ status: 401, title: 'Invalid credentials' }, { status: 401, statusText: 'Unauthorized' });

    expect(confirmedSpy).toHaveBeenCalled();
  });

  it('shows an invalid-code message on a 400', () => {
    const fixture = createAndSubmit('000000');

    httpMock
      .expectOne(IdentityAuthEndpoints.confirmEmail)
      .flush({ status: 400, title: 'Invalid verification code' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('incorrect or has expired');
  });

  it('resends the code to the same email', () => {
    const fixture = TestBed.createComponent(ConfirmEmail);
    fixture.componentRef.setInput('email', 'newplayer@example.com');
    fixture.componentRef.setInput('password', 'a-strong-password');
    fixture.detectChanges();

    const resendButton = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find(
      (button) => button.textContent?.includes('Resend code'),
    )!;
    resendButton.dispatchEvent(new Event('click'));

    const request = httpMock.expectOne(IdentityAuthEndpoints.resendVerification);
    expect(request.request.body).toEqual({ email: 'newplayer@example.com', gameSlug: 'demo-shooter' });
    request.flush(null, { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('A new code was sent');
  });

  it('counts down the resend cooldown and re-enables the button at zero', () => {
    const fixture = TestBed.createComponent(ConfirmEmail);
    fixture.componentRef.setInput('email', 'newplayer@example.com');
    fixture.componentRef.setInput('password', 'a-strong-password');
    fixture.detectChanges();

    const findResendButton = () =>
      Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((button) =>
        button.textContent?.includes('Resend'),
      )!;

    findResendButton().dispatchEvent(new Event('click'));
    httpMock
      .expectOne(IdentityAuthEndpoints.resendVerification)
      .flush(null, { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();

    expect(findResendButton().disabled).toBe(true);
    expect(findResendButton().textContent).toContain('Resend in 30s');

    vi.advanceTimersByTime(5_000);
    fixture.detectChanges();
    expect(findResendButton().textContent).toContain('Resend in 25s');

    vi.advanceTimersByTime(25_000);
    fixture.detectChanges();
    expect(findResendButton().disabled).toBe(false);
    expect(findResendButton().textContent).toContain('Resend code');
  });

  it('advances focus to the next box as each digit is entered', () => {
    const fixture = TestBed.createComponent(ConfirmEmail);
    fixture.componentRef.setInput('email', 'newplayer@example.com');
    fixture.componentRef.setInput('password', 'a-strong-password');
    fixture.detectChanges();

    const digitInputs = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('.code-digit'),
    ) as HTMLInputElement[];

    digitInputs[0].value = '1';
    digitInputs[0].dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(document.activeElement).toBe(digitInputs[1]);
  });

  it('fills every box from a single pasted code', () => {
    const fixture = TestBed.createComponent(ConfirmEmail);
    fixture.componentRef.setInput('email', 'newplayer@example.com');
    fixture.componentRef.setInput('password', 'a-strong-password');
    fixture.detectChanges();

    const digitInputs = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('.code-digit'),
    ) as HTMLInputElement[];

    const clipboardData = { getData: () => '123456' } as unknown as DataTransfer;
    digitInputs[0].dispatchEvent(
      Object.assign(new Event('paste', { cancelable: true }), { clipboardData }),
    );
    fixture.detectChanges();

    expect(digitInputs.map((input) => input.value)).toEqual(['1', '2', '3', '4', '5', '6']);
  });
});
