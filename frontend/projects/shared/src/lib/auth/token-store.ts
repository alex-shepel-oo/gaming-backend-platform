import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class TokenStore {
  private readonly accessToken = signal<string | null>(null);

  read(): string | null {
    return this.accessToken();
  }

  isAuthenticated(): boolean {
    return this.accessToken() !== null;
  }

  set(accessToken: string): void {
    this.accessToken.set(accessToken);
  }

  clear(): void {
    this.accessToken.set(null);
  }
}
