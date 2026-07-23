import { Component, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatTabsModule } from '@angular/material/tabs';
import { RegistrationAcceptedResponse } from 'shared';
import { ConfirmEmail } from '../confirm-email/confirm-email';
import { Login } from '../login/login';
import { Register } from '../register/register';

type AuthShellState = 'auth' | 'confirm';

@Component({
  selector: 'app-auth-shell',
  imports: [MatCardModule, MatTabsModule, Login, Register, ConfirmEmail],
  templateUrl: './auth-shell.html',
  styleUrl: './auth-shell.scss',
})
export class AuthShell {
  protected readonly state = signal<AuthShellState>('auth');
  protected readonly selectedTabIndex = signal(0);
  protected readonly notice = signal<string | null>(null);
  protected readonly pendingEmail = signal<string | null>(null);

  protected onRegistered(response: RegistrationAcceptedResponse): void {
    if (response.verificationRequired) {
      this.notice.set(null);
      this.pendingEmail.set(response.email);
      this.state.set('confirm');

      return;
    }

    this.notice.set('Registration complete. You can log in now.');
    this.selectedTabIndex.set(0);
  }

  protected onConfirmed(): void {
    this.pendingEmail.set(null);
    this.state.set('auth');
    this.selectedTabIndex.set(0);
    this.notice.set('Email confirmed. You can log in now.');
  }

  protected onBackToLogin(): void {
    this.pendingEmail.set(null);
    this.state.set('auth');
    this.selectedTabIndex.set(0);
  }
}
