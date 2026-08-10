import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, Router } from '@angular/router';
import { TokenStore } from './token-store';

function allowedRoles(route: ActivatedRouteSnapshot): readonly string[] {
  return (route.data['roles'] as string[] | undefined) ?? [];
}

export const roleGuard: CanActivateFn = (route) => {
  const tokenStore = inject(TokenStore);
  const router = inject(Router);

  const role = tokenStore.claims()?.role ?? null;

  return (role !== null && allowedRoles(route).includes(role)) || router.createUrlTree(['/login']);
};
