import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService, DEFAULT_GAME_SLUG } from 'shared';

const RESEND_COOLDOWN_MS = 30_000;

type ConfirmError = 'invalid-code' | 'unknown';

function classifyConfirmError(error: unknown): ConfirmError {
  if (error instanceof HttpErrorResponse && error.status === 400) {
    return 'invalid-code';
  }

  return 'unknown';
}

@Component({
  selector: 'app-confirm-email',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './confirm-email.html',
  styleUrl: './confirm-email.scss',
})
export class ConfirmEmail {
  private readonly formBuilder = inject(FormBuilder);
  private readonly authService = inject(AuthService);

  readonly email = input.required<string>();
  readonly confirmed = output<void>();
  readonly backToLogin = output<void>();

  protected readonly form = this.formBuilder.nonNullable.group({
    code: ['', [Validators.required, Validators.pattern(/^\d{6}$/)]],
  });

  protected readonly submitting = signal(false);
  protected readonly error = signal<ConfirmError | null>(null);
  protected readonly resending = signal(false);
  protected readonly resent = signal(false);

  protected submit(): void {
    if (this.form.invalid) {
      return;
    }

    this.submitting.set(true);
    this.error.set(null);

    this.authService.confirmEmail({ email: this.email(), code: this.form.getRawValue().code }).subscribe({
      next: () => {
        this.submitting.set(false);
        this.confirmed.emit();
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.error.set(classifyConfirmError(error));
      },
    });
  }

  protected resend(): void {
    this.resending.set(true);
    this.resent.set(false);

    this.authService.resendVerification({ email: this.email(), gameSlug: DEFAULT_GAME_SLUG }).subscribe({
      next: () => this.onResendSettled(),
      error: () => this.onResendSettled(),
    });
  }

  private onResendSettled(): void {
    this.resent.set(true);
    setTimeout(() => this.resending.set(false), RESEND_COOLDOWN_MS);
  }
}
