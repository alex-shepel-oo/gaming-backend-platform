import { TokenStore } from './token-store';

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

describe('TokenStore claims', () => {
  let tokenStore: TokenStore;

  beforeEach(() => {
    tokenStore = new TokenStore();
  });

  it('has no claims before a token is set', () => {
    expect(tokenStore.claims()).toBeNull();
  });

  it('decodes email, display name and role from the access token', () => {
    tokenStore.set(
      buildFakeToken({ sub: 'user-1', email: 'player@example.com', name: 'Player One', role: 'Player' }),
    );

    expect(tokenStore.claims()).toEqual({
      userId: 'user-1',
      email: 'player@example.com',
      displayName: 'Player One',
      role: 'Player',
      gameId: null,
    });
  });

  it('includes gameId when the token is game-scoped', () => {
    tokenStore.set(
      buildFakeToken({
        sub: 'user-1',
        email: 'player@example.com',
        name: 'Player One',
        role: 'Player',
        game_id: 'game-1',
      }),
    );

    expect(tokenStore.claims()?.gameId).toBe('game-1');
  });

  it('returns null for a malformed token instead of throwing', () => {
    tokenStore.set('not-a-jwt');

    expect(tokenStore.claims()).toBeNull();
  });

  it('clears claims on clear()', () => {
    tokenStore.set(buildFakeToken({ sub: 'user-1', email: 'player@example.com', name: 'Player One', role: 'Player' }));

    tokenStore.clear();

    expect(tokenStore.claims()).toBeNull();
  });
});
