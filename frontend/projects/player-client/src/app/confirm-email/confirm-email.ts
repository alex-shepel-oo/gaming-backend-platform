import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, input, output, signal, viewChildren } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router } from '@angular/router';
import { Subscription, interval } from 'rxjs';
import { take } from 'rxjs/operators';
import { AuthService, DEFAULT_GAME_SLUG } from 'shared';

const CODE_LENGTH = 6;
const RESEND_COOLDOWN_SECONDS = 30;

type ConfirmError = 'invalid-code' | 'unknown';

function classifyConfirmError(error: unknown): ConfirmError {
  if (error instanceof HttpErrorResponse && error.status === 400) {
    return 'invalid-code';
  }

  return 'unknown';
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-confirm-email',
  imports: [ReactiveFormsModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule],
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

  protected readonly digits = this.formBuilder.nonNullable.array(
    Array.from({ length: CODE_LENGTH }, () =>
      this.formBuilder.nonNullable.control('', [Validators.required, Validators.pattern(/^\d$/)]),
    ),
  );

  protected readonly form = this.formBuilder.group({ digits: this.digits });

  private readonly digitInputs = viewChildren<ElementRef<HTMLInputElement>>('digitInput');

  protected readonly submitting = signal(false);
  protected readonly error = signal<ConfirmError | null>(null);
  protected readonly resending = signal(false);
  protected readonly resent = signal(false);
  protected readonly secondsRemaining = signal(0);

  private countdownSubscription: Subscription | null = null;

  constructor() {
    this.destroyRef.onDestroy(() => this.countdownSubscription?.unsubscribe());
  }

  protected onDigitInput(event: Event, index: number): void {
    const raw = (event.target as HTMLInputElement).value;
    const digit = raw.replace(/\D/g, '').slice(-1);

    this.digits.at(index).setValue(digit);

    if (digit && index < CODE_LENGTH - 1) {
      this.focusDigit(index + 1);
    }
  }

  protected onDigitKeydown(event: KeyboardEvent, index: number): void {
    if (event.key === 'Backspace' && !this.digits.at(index).value && index > 0) {
      this.focusDigit(index - 1);
    }
  }

  protected onPaste(event: ClipboardEvent): void {
    event.preventDefault();

    const pasted = event.clipboardData?.getData('text').replace(/\D/g, '').slice(0, CODE_LENGTH) ?? '';

    pasted.split('').forEach((char, index) => this.digits.at(index)?.setValue(char));

    if (pasted.length > 0) {
      this.focusDigit(Math.min(pasted.length, CODE_LENGTH) - 1);
    }
  }

  private focusDigit(index: number): void {
    this.digitInputs()[index]?.nativeElement.focus();
  }

  protected submit(): void {
    if (this.digits.invalid) {
      return;
    }

    this.submitting.set(true);
    this.error.set(null);

    const code = this.digits.controls.map((control) => control.value).join('');

    this.authService.confirmEmail({ email: this.email(), code }).subscribe({
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
