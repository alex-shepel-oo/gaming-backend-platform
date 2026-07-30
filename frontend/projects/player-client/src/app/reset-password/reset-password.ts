import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from 'shared';

type ResetError = 'invalid-token' | 'unknown';

function classifyResetError(error: unknown): ResetError {
  if (error instanceof HttpErrorResponse && error.status === 400) {
    return 'invalid-token';
  }

  return 'unknown';
}

@Component({
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

  protected readonly hideNewPassword = signal(true);
  protected readonly hideConfirmPassword = signal(true);

  protected toggleNewPasswordVisibility(): void {
    this.hideNewPassword.set(!this.hideNewPassword());
  }

  protected toggleConfirmPasswordVisibility(): void {
    this.hideConfirmPassword.set(!this.hideConfirmPassword());
  }

  constructor() {
    this.resetForm.controls.newPassword.valueChanges.subscribe(() =>
      this.resetForm.controls.confirmPassword.updateValueAndValidity(),
    );
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

    this.authService
      .resetPassword({ token: this.token, newPassword: this.resetForm.getRawValue().newPassword })
      .subscribe({
        next: () => {
          this.resetSubmitting.set(false);
          this.resetSucceeded.set(true);
        },
        error: (error: unknown) => {
          this.resetSubmitting.set(false);
          this.resetError.set(classifyResetError(error));
        },
      });
  }
}
