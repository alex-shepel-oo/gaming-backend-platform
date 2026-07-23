import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { TokenStore } from './token-store';

// Mirror of authGuard for the opposite direction: keeps an already-authenticated
// visitor off /login (UX only, same caveat as authGuard -- see ADR-0012).
export const guestGuard: CanActivateFn = () => {
  const tokenStore = inject(TokenStore);
  const router = inject(Router);

  return !tokenStore.isAuthenticated() || router.createUrlTree(['/games']);
};
