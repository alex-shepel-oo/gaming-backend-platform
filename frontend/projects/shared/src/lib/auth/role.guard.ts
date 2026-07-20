import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, Router } from '@angular/router';
import { TokenStore } from './token-store';

function decodeRoleClaim(accessToken: string): string | null {
  try {
    const payload = accessToken.split('.')[1];
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'));

    return (JSON.parse(json) as { role?: string }).role ?? null;
  } catch {
    return null;
  }
}

function allowedRoles(route: ActivatedRouteSnapshot): readonly string[] {
  return (route.data['roles'] as string[] | undefined) ?? [];
}

export const roleGuard: CanActivateFn = (route) => {
  const tokenStore = inject(TokenStore);
  const router = inject(Router);

  const accessToken = tokenStore.read();
  const role = accessToken ? decodeRoleClaim(accessToken) : null;

  return (role !== null && allowedRoles(route).includes(role)) || router.createUrlTree(['/login']);
};
