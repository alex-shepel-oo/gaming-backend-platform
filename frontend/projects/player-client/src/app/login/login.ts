import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router, RouterLink } from '@angular/router';
import { AuthService, EMAIL_NOT_CONFIRMED_PROBLEM_TYPE } from 'shared';

type LoginError = 'invalid-credentials' | 'email-not-confirmed';

function classifyLoginError(error: unknown): LoginError {
  if (error instanceof HttpErrorResponse && error.status === 403) {
    const problemType = (error.error as { type?: string } | null)?.type;

    if (problemType === EMAIL_NOT_CONFIRMED_PROBLEM_TYPE) {
      return 'email-not-confirmed';
    }
  }

  return 'invalid-credentials';
}

@Component({
  selector: 'app-login',
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    RouterLink,
  ],
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class Login {
  private readonly formBuilder = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly form = this.formBuilder.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  protected readonly submitting = signal(false);
  protected readonly error = signal<LoginError | null>(null);

  protected submit(): void {
    if (this.form.invalid) {
      return;
    }

    this.submitting.set(true);
    this.error.set(null);

    this.authService.login(this.form.getRawValue()).subscribe({
      next: () => this.router.navigateByUrl('/games'),
      error: (error: unknown) => {
        this.submitting.set(false);
        this.error.set(classifyLoginError(error));
      },
    });
  }
}
