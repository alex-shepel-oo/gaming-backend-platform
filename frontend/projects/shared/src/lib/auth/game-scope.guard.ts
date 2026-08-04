import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { TokenStore } from './token-store';

export const gameScopeGuard: CanActivateFn = () => {
  const tokenStore = inject(TokenStore);
  const router = inject(Router);

  return tokenStore.claims()?.scope === 'Game' || router.createUrlTree(['/games']);
};
