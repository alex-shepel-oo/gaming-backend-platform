import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { IdentityAuthEndpoints } from 'shared';
import { ConfirmEmail } from './confirm-email';

describe('ConfirmEmail', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();

    TestBed.configureTestingModule({
      imports: [ConfirmEmail],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  function createAndSubmit(code: string): ComponentFixture<ConfirmEmail> {
    const fixture = TestBed.createComponent(ConfirmEmail);
    fixture.componentRef.setInput('email', 'newplayer@example.com');
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const codeInput = element.querySelector('input') as HTMLInputElement;
    codeInput.value = code;
    codeInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (element.querySelector('form') as HTMLFormElement).dispatchEvent(
      new Event('submit', { cancelable: true }),
    );

    return fixture;
  }

  it('emits confirmed on a successful confirmation', () => {
    const fixture = createAndSubmit('123456');
    const confirmedSpy = vi.fn();
    fixture.componentInstance.confirmed.subscribe(confirmedSpy);

    const request = httpMock.expectOne(IdentityAuthEndpoints.confirmEmail);
    expect(request.request.body).toEqual({ email: 'newplayer@example.com', code: '123456' });
    request.flush(null);

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
});
