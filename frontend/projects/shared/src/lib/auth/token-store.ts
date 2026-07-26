import { Injectable, computed, signal } from '@angular/core';

export interface AccessTokenClaims {
  userId: string;
  email: string;
  displayName: string;
  role: string | null;
  scope: string;
  gameId: string | null;
}

// Decodes the payload segment only, for display purposes -- this is UX, not
// a security decision (same posture as the route guards, ADR-0012): the
// signature is never checked here, the backend is what actually trusts or
// rejects the token.
function decodeJwtPayload(token: string): Record<string, unknown> | null {
  const segments = token.split('.');

  if (segments.length !== 3) {
    return null;
  }

  try {
    const base64 = segments[1].replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=');
    const bytes = Uint8Array.from(atob(padded), (char) => char.charCodeAt(0));

    return JSON.parse(new TextDecoder().decode(bytes)) as Record<string, unknown>;
  } catch {
    return null;
  }
}

function toClaims(payload: Record<string, unknown>): AccessTokenClaims | null {
  const { sub, email, name, role, scope, game_id: gameId } = payload as Record<string, unknown>;

  if (typeof sub !== 'string' || typeof email !== 'string' || typeof name !== 'string' || typeof scope !== 'string') {
    return null;
  }

  return {
    userId: sub,
    email,
    displayName: name,
    role: typeof role === 'string' ? role : null,
    scope,
    gameId: typeof gameId === 'string' ? gameId : null,
  };
}

@Injectable({ providedIn: 'root' })
export class TokenStore {
  private readonly accessToken = signal<string | null>(null);

  readonly claims = computed<AccessTokenClaims | null>(() => {
    const token = this.accessToken();
    const payload = token ? decodeJwtPayload(token) : null;

    return payload ? toClaims(payload) : null;
  });

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
