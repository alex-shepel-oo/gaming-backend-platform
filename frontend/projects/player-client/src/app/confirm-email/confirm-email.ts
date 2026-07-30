import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router } from '@angular/router';
import { Subscription, interval } from 'rxjs';
import { take } from 'rxjs/operators';
import { AuthService, DEFAULT_GAME_SLUG } from 'shared';

const RESEND_COOLDOWN_SECONDS = 30;

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
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);

  readonly email = input.required<string>();
  readonly password = input.required<string>();
  readonly confirmed = output<void>();
  readonly backToLogin = output<void>();

  protected readonly form = this.formBuilder.nonNullable.group({
    code: ['', [Validators.required, Validators.pattern(/^\d{6}$/)]],
  });

  protected readonly submitting = signal(false);
  protected readonly error = signal<ConfirmError | null>(null);
  protected readonly resending = signal(false);
  protected readonly resent = signal(false);
  protected readonly secondsRemaining = signal(0);

  private countdownSubscription: Subscription | null = null;

  constructor() {
    this.destroyRef.onDestroy(() => this.countdownSubscription?.unsubscribe());
  }

  protected submit(): void {
    if (this.form.invalid) {
      return;
    }

    this.submitting.set(true);
    this.error.set(null);

    this.authService.confirmEmail({ email: this.email(), code: this.form.getRawValue().code }).subscribe({
      next: () => this.autoLogin(),
      error: (error: unknown) => {
        this.submitting.set(false);
        this.error.set(classifyConfirmError(error));
      },
    });
  }

  private autoLogin(): void {
    this.authService.login({ email: this.email(), password: this.password() }).subscribe({
      next: () => {
        this.submitting.set(false);
        this.router.navigateByUrl('/games');
      },
      error: () => {
        this.submitting.set(false);
        this.confirmed.emit();
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
    this.startCountdown();
  }

  private startCountdown(): void {
    this.countdownSubscription?.unsubscribe();
    this.secondsRemaining.set(RESEND_COOLDOWN_SECONDS);

    this.countdownSubscription = interval(1000)
      .pipe(take(RESEND_COOLDOWN_SECONDS))
      .subscribe({
        next: () => this.secondsRemaining.update((seconds) => seconds - 1),
        complete: () => this.resending.set(false),
      });
  }
}
