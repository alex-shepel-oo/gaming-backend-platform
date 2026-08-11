import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConfirmEmail } from '../confirm-email/confirm-email';
import { Login } from '../login/login';
import { Register, RegisteredEvent } from '../register/register';

type AuthShellState = 'auth' | 'confirm';
type AuthTab = 'login' | 'register';

interface PendingRegistration {
  email: string;
  password: string;
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-auth-shell',
  imports: [Login, Register, ConfirmEmail, RouterLink],
  templateUrl: './auth-shell.html',
  styleUrl: './auth-shell.scss',
})
export class AuthShell {
  protected readonly state = signal<AuthShellState>('auth');
  protected readonly activeTab = signal<AuthTab>('login');
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
    this.activeTab.set('login');
  }

  protected onConfirmed(): void {
    this.pendingRegistration.set(null);
    this.state.set('auth');
    this.activeTab.set('login');
    this.notice.set('Email confirmed. You can log in now.');
  }

  protected onBackToLogin(): void {
    this.pendingRegistration.set(null);
    this.state.set('auth');
    this.activeTab.set('login');
  }
}
