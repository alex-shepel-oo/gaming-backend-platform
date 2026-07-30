import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService, DEFAULT_GAME_SLUG, RegistrationAcceptedResponse } from 'shared';

export interface RegisteredEvent {
  response: RegistrationAcceptedResponse;
  password: string;
}

type RegisterError = 'validation' | 'game-not-found' | 'email-taken' | 'rate-limited' | 'unknown';

function classifyRegisterError(error: unknown): RegisterError {
  if (error instanceof HttpErrorResponse) {
    switch (error.status) {
      case 400:
        return 'validation';
      case 404:
        return 'game-not-found';
      case 409:
        return 'email-taken';
      case 429:
        return 'rate-limited';
      default:
        return 'unknown';
    }
  }

  return 'unknown';
}

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './register.html',
  styleUrl: './register.scss',
})
export class Register {
  private readonly formBuilder = inject(FormBuilder);
  private readonly authService = inject(AuthService);

  protected readonly form = this.formBuilder.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    displayName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(64)]],
    password: ['', [Validators.required, Validators.minLength(12), Validators.maxLength(128)]],
  });

  protected readonly submitting = signal(false);
  protected readonly error = signal<RegisterError | null>(null);

  readonly registered = output<RegisteredEvent>();

  protected submit(): void {
    if (this.form.invalid) {
      return;
    }

    this.submitting.set(true);
    this.error.set(null);

    const { password } = this.form.getRawValue();

    this.authService.register({ ...this.form.getRawValue(), gameSlug: DEFAULT_GAME_SLUG }).subscribe({
      next: (response) => {
        this.submitting.set(false);
        this.form.reset();
        this.registered.emit({ response, password });
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.error.set(classifyRegisterError(error));
      },
    });
  }
}
