import { Component, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatTabsModule } from '@angular/material/tabs';
import { ConfirmEmail } from '../confirm-email/confirm-email';
import { Login } from '../login/login';
import { Register, RegisteredEvent } from '../register/register';

type AuthShellState = 'auth' | 'confirm';

interface PendingRegistration {
  email: string;
  password: string;
}

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
  protected readonly pendingRegistration = signal<PendingRegistration | null>(null);

  protected onRegistered(event: RegisteredEvent): void {
    if (event.response.verificationRequired) {
      this.notice.set(null);
      this.pendingRegistration.set({ email: event.response.email, password: event.password });
      this.state.set('confirm');

      return;
    }

    this.notice.set('Registration complete. You can log in now.');
    this.selectedTabIndex.set(0);
  }

  protected onConfirmed(): void {
    this.pendingRegistration.set(null);
    this.state.set('auth');
    this.selectedTabIndex.set(0);
    this.notice.set('Email confirmed. You can log in now.');
  }

  protected onBackToLogin(): void {
    this.pendingRegistration.set(null);
    this.state.set('auth');
    this.selectedTabIndex.set(0);
  }
}
