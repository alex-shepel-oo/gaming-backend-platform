import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, provideRouter, Router, RouterStateSnapshot } from '@angular/router';
import { guestGuard } from './guest.guard';
import { TokenStore } from './token-store';

describe('guestGuard', () => {
  let tokenStore: TokenStore;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });

    tokenStore = TestBed.inject(TokenStore);
    router = TestBed.inject(Router);
  });

  function run() {
    return TestBed.runInInjectionContext(() =>
      guestGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
    );
  }

  it('allows an unauthenticated visitor onto the login screen', () => {
    expect(run()).toBe(true);
  });

  it('redirects an already-authenticated visitor away from login', () => {
    tokenStore.set('valid-access-token');

    const result = run();

    expect(result).toEqual(router.createUrlTree(['/games']));
  });
});
