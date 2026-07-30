import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router } from '@angular/router';
import { AuthService, EMAIL_NOT_CONFIRMED_PROBLEM_TYPE, TokenStore } from 'shared';

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

// Admin login is always account-first: there is no game-slug field, unlike
// the game-specific logins of an earlier era of player-client. A platform
// role lands the caller straight in; anyone without one (e.g. a Game-Admin
// scoped to a single game) needs to pick which game to act on first -- that
// picker is a separate route (select-game), not part of this component.
@Component({
  selector: 'admin-login',
  imports: [
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './admin-login.html',
  styleUrl: './admin-login.scss',
})
export class AdminLogin {
  private readonly formBuilder = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly tokenStore = inject(TokenStore);
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
      next: () => this.routeAfterLogin(),
      error: (error: unknown) => {
        this.submitting.set(false);
        this.error.set(classifyLoginError(error));
      },
    });
  }

  private routeAfterLogin(): void {
    const scope = this.tokenStore.claims()?.scope;

    // 'account' -- no platform role yet, so the caller has to pick which
    // game to act as an admin/moderator for. 'platform' and 'game' (the
    // latter shouldn't normally happen straight off a slug-less login, but
    // if it does, it's already scoped into something usable) both go
    // straight into the app.
    if (scope === 'Account') {
      this.router.navigateByUrl('/select-game');

      return;
    }

    this.router.navigateByUrl('/dashboard');
  }
}
