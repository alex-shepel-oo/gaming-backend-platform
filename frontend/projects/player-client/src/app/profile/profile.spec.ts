import { TestBed } from '@angular/core/testing';
import { TokenStore } from 'shared';
import { Profile } from './profile';

function base64UrlEncode(value: string): string {
  const bytes = new TextEncoder().encode(value);
  let binary = '';
  bytes.forEach((byte) => {
    binary += String.fromCharCode(byte);
  });

  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function buildFakeToken(payload: Record<string, unknown>): string {
  const header = base64UrlEncode(JSON.stringify({ alg: 'none' }));
  const body = base64UrlEncode(JSON.stringify(payload));

  return `${header}.${body}.signature`;
}

describe('Profile', () => {
  it('shows the real email, display name and role from the access token', () => {
    TestBed.inject(TokenStore).set(
      buildFakeToken({ sub: 'user-1', email: 'player@example.com', name: 'Player One', role: 'Player' }),
    );

    const fixture = TestBed.createComponent(Profile);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Player One');
    expect(text).toContain('player@example.com');
    expect(text).toContain('Player');
  });

  it('shows a not-available note for fields the token does not carry', () => {
    TestBed.inject(TokenStore).set(
      buildFakeToken({ sub: 'user-1', email: 'player@example.com', name: 'Player One', role: 'Player' }),
    );

    const fixture = TestBed.createComponent(Profile);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain("Join date isn't available yet");
  });

  it('shows an error message when there are no usable claims', () => {
    const fixture = TestBed.createComponent(Profile);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain("couldn't read your account details");
  });
});
