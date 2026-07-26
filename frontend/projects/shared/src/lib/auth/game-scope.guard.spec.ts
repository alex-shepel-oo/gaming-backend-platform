import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { gameScopeGuard } from './game-scope.guard';
import { TokenStore } from './token-store';

function fakeAccessToken(scope: string): string {
  const payload = { sub: 'user-1', email: 'player@example.com', name: 'Player One', scope };

  return `header.${btoa(JSON.stringify(payload))}.signature`;
}

// gameScopeGuard only decides what to render -- it is UX, not the security
// boundary. The backend rejects an unauthorized request on its own
// regardless of what this guard decides (ADR-0012).
describe('gameScopeGuard', () => {
  let tokenStore: TokenStore;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });

    tokenStore = TestBed.inject(TokenStore);
    router = TestBed.inject(Router);
  });

  function run() {
    return TestBed.runInInjectionContext(() => gameScopeGuard({} as never, {} as never));
  }

  it('allows navigation for a game-scoped session', () => {
    tokenStore.set(fakeAccessToken('game'));

    expect(run()).toBe(true);
  });

  it('redirects an account-scoped session to /games', () => {
    tokenStore.set(fakeAccessToken('account'));

    const result = run();

    expect(result).toEqual(router.createUrlTree(['/games']));
  });

  it('redirects to /games when there is no session at all', () => {
    const result = run();

    expect(result).toEqual(router.createUrlTree(['/games']));
  });
});
