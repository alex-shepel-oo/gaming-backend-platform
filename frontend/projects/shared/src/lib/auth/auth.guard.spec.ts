import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, provideRouter, Router, RouterStateSnapshot } from '@angular/router';
import { authGuard } from './auth.guard';
import { TokenStore } from './token-store';

// authGuard only decides what to render -- it is UX, not the security
// boundary. The backend rejects an unauthorized request on its own
// regardless of what this guard decides (ADR-0012).
describe('authGuard', () => {
  let tokenStore: TokenStore;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });

    tokenStore = TestBed.inject(TokenStore);
    router = TestBed.inject(Router);
  });

  function run() {
    return TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
    );
  }

  it('redirects an unauthenticated visitor to Login', () => {
    const result = run();

    expect(result).toEqual(router.createUrlTree(['/login']));
  });

  it('allows navigation once a session is established', () => {
    tokenStore.set('valid-access-token');

    expect(run()).toBe(true);
  });
});
