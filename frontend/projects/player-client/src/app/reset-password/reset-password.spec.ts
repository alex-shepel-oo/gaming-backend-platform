import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { IdentityAuthEndpoints } from 'shared';
import { ResetPassword } from './reset-password';

describe('ResetPassword', () => {
  let httpMock: HttpTestingController;

  afterEach(() => {
    httpMock.verify();
  });

  describe('without a token in the URL', () => {
    beforeEach(() => {
      TestBed.configureTestingModule({
        imports: [ResetPassword],
        providers: [
          provideHttpClient(),
          provideHttpClientTesting(),
          { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({}) } } },
        ],
      });

      httpMock = TestBed.inject(HttpTestingController);
    });

    it('submits the email and shows a neutral notice on success', () => {
      const fixture = TestBed.createComponent(ResetPassword);
      fixture.detectChanges();

      const element = fixture.nativeElement as HTMLElement;
      const emailInput = element.querySelector('input[type="email"]') as HTMLInputElement;
      emailInput.value = 'player@example.com';
      emailInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      (element.querySelector('form') as HTMLFormElement).dispatchEvent(
        new Event('submit', { cancelable: true }),
      );

      const request = httpMock.expectOne(IdentityAuthEndpoints.requestPasswordReset);
      expect(request.request.body).toEqual({ email: 'player@example.com' });
      request.flush(null, { status: 202, statusText: 'Accepted' });
      fixture.detectChanges();

      const text = element.textContent ?? '';
      expect(text).toContain('If an account exists for that email, a reset link has been sent.');
    });
  });

  describe('with a token in the URL', () => {
    beforeEach(() => {
      TestBed.configureTestingModule({
        imports: [ResetPassword],
        providers: [
          provideHttpClient(),
          provideHttpClientTesting(),
          {
            provide: ActivatedRoute,
            useValue: { snapshot: { queryParamMap: convertToParamMap({ token: 'the-reset-token' }) } },
          },
        ],
      });

      httpMock = TestBed.inject(HttpTestingController);
    });

    function createAndSubmit(password: string, confirmPassword: string): ComponentFixture<ResetPassword> {
      const fixture = TestBed.createComponent(ResetPassword);
      fixture.detectChanges();

      const element = fixture.nativeElement as HTMLElement;
      const passwordInputs = element.querySelectorAll('input[type="password"]');
      const newPasswordInput = passwordInputs[0] as HTMLInputElement;
      const confirmPasswordInput = passwordInputs[1] as HTMLInputElement;

      newPasswordInput.value = password;
      newPasswordInput.dispatchEvent(new Event('input'));
      confirmPasswordInput.value = confirmPassword;
      confirmPasswordInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      (element.querySelector('form') as HTMLFormElement).dispatchEvent(
        new Event('submit', { cancelable: true }),
      );

      return fixture;
    }

    it('submits the token and new password and shows success on 204', () => {
      const fixture = createAndSubmit('a-new-strong-password', 'a-new-strong-password');

      const request = httpMock.expectOne(IdentityAuthEndpoints.resetPassword);
      expect(request.request.body).toEqual({ token: 'the-reset-token', newPassword: 'a-new-strong-password' });
      request.flush(null, { status: 204, statusText: 'No Content' });
      fixture.detectChanges();

      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('Your password has been reset');
    });

    it('shows a neutral invalid-link message on a 400', () => {
      const fixture = createAndSubmit('a-new-strong-password', 'a-new-strong-password');

      httpMock
        .expectOne(IdentityAuthEndpoints.resetPassword)
        .flush({ status: 400, title: 'Invalid token' }, { status: 400, statusText: 'Bad Request' });
      fixture.detectChanges();

      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('This reset link is invalid or has expired');
    });

    it('does not submit when the passwords do not match', () => {
      createAndSubmit('a-new-strong-password', 'a-different-password');

      httpMock.expectNone(IdentityAuthEndpoints.resetPassword);
    });
  });
});
