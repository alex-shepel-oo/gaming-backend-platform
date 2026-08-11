import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from 'shared';

type ResetError = 'invalid-token' | 'unknown';

function classifyResetError(error: unknown): ResetError {
  if (error instanceof HttpErrorResponse && error.status === 400) {
    return 'invalid-token';
  }

  return 'unknown';
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-reset-password',
  imports: [
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    RouterLink,
  ],
  templateUrl: './reset-password.html',
  styleUrl: './reset-password.scss',
})
export class ResetPassword {
  private readonly formBuilder = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly token = inject(ActivatedRoute).snapshot.queryParamMap.get('token');

  private readonly passwordsMatchValidator: ValidatorFn = (control) => {
    const newPassword = control.parent?.get('newPassword')?.value as string | undefined;

    return newPassword !== undefined && control.value !== newPassword ? { passwordMismatch: true } : null;
  };

  protected readonly requestForm = this.formBuilder.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });

  protected readonly requestSubmitting = signal(false);
  protected readonly requestSent = signal(false);
  protected readonly requestFailed = signal(false);

  protected readonly resetForm = this.formBuilder.nonNullable.group({
    newPassword: ['', [Validators.required, Validators.minLength(12), Validators.maxLength(128)]],
    confirmPassword: ['', [Validators.required, this.passwordsMatchValidator]],
  });

  protected readonly resetSubmitting = signal(false);
  protected readonly resetSucceeded = signal(false);
  protected readonly resetError = signal<ResetError | null>(null);

  // Only meaningful while token is set. Starts true so the page shows a
  // spinner instead of the form for the instant it takes to find out the
  // link is already dead, rather than only discovering that after the
  // player has typed a new password and hit submit.
  protected readonly tokenChecking = signal(true);

  protected readonly hideNewPassword = signal(true);
  protected readonly hideConfirmPassword = signal(true);

  protected toggleNewPasswordVisibility(): void {
    this.hideNewPassword.set(!this.hideNewPassword());
  }

  protected toggleConfirmPasswordVisibility(): void {
    this.hideConfirmPassword.set(!this.hideConfirmPassword());
  }

  constructor() {
    const destroyRef = inject(DestroyRef);

    this.resetForm.controls.newPassword.valueChanges.pipe(takeUntilDestroyed(destroyRef)).subscribe(() =>
      this.resetForm.controls.confirmPassword.updateValueAndValidity(),
    );

    if (this.token) {
      this.authService.validateResetToken(this.token).subscribe({
        next: () => this.tokenChecking.set(false),
        error: (error: unknown) => {
          this.tokenChecking.set(false);

          // A non-400 failure here (network blip, 5xx) doesn't necessarily
          // mean the token itself is bad. Fail open into the form rather
          // than telling the player their link is dead over a transient
          // error; the real reset attempt is still the source of truth.
          if (classifyResetError(error) === 'invalid-token') {
            this.resetError.set('invalid-token');
          }
        },
      });
    }
  }

  protected submitRequest(): void {
    if (this.requestForm.invalid) {
      return;
    }

    this.requestSubmitting.set(true);
    this.requestFailed.set(false);

    this.authService.requestPasswordReset(this.requestForm.getRawValue()).subscribe({
      next: () => {
        this.requestSubmitting.set(false);
        this.requestSent.set(true);
      },
      error: () => {
        this.requestSubmitting.set(false);
        this.requestFailed.set(true);
      },
    });
  }

  protected submitReset(): void {
    if (this.resetForm.invalid || !this.token) {
      return;
    }

    this.resetSubmitting.set(true);
    this.resetError.set(null);

    const newPassword = this.resetForm.getRawValue().newPassword;

    this.authService.resetPassword({ token: this.token, newPassword }).subscribe({
      next: (response) => this.autoLogin(response.email, newPassword),
      error: (error: unknown) => {
        this.resetSubmitting.set(false);
        this.resetError.set(classifyResetError(error));
      },
    });
  }

  private autoLogin(email: string, password: string): void {
    this.authService.login({ email, password }).subscribe({
      next: () => {
        this.resetSubmitting.set(false);
        this.router.navigateByUrl('/games');
      },
      error: () => {
        this.resetSubmitting.set(false);
        this.resetSucceeded.set(true);
      },
    });
  }
}
