import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, provideRouter, Router, RouterStateSnapshot } from '@angular/router';
import { GUEST_REDIRECT_PATH, guestGuard, TokenStore } from 'shared';

describe('admin-client guestGuard wiring', () => {
  it('redirects an already-authenticated visitor hitting /login to /dashboard, not /games', () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: GUEST_REDIRECT_PATH, useValue: '/dashboard' }],
    });

    const tokenStore = TestBed.inject(TokenStore);
    const router = TestBed.inject(Router);
    tokenStore.set('valid-access-token');

    const result = TestBed.runInInjectionContext(() =>
      guestGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
    );

    expect(result).toEqual(router.createUrlTree(['/dashboard']));
  });
});
