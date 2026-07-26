import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { GUEST_REDIRECT_PATH } from './guest-redirect-path';
import { TokenStore } from './token-store';

// Mirror of authGuard for the opposite direction: keeps an already-authenticated
// visitor off /login (UX only, same caveat as authGuard -- see ADR-0012).
export const guestGuard: CanActivateFn = () => {
  const tokenStore = inject(TokenStore);
  const router = inject(Router);
  const redirectPath = inject(GUEST_REDIRECT_PATH);

  return !tokenStore.isAuthenticated() || router.createUrlTree([redirectPath]);
};
