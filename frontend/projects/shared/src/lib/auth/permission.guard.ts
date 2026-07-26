import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, Router } from '@angular/router';
import { TokenStore } from './token-store';

function requiredPermission(route: ActivatedRouteSnapshot): string | undefined {
  return route.data['permission'] as string | undefined;
}

// UX-only, same posture as role.guard.ts (ADR-0012): this only decides what
// admin-client renders. The backend's own permission checks (and the
// anti-escalation guard on role-permission writes) are the actual boundary
// and enforce themselves regardless of what this guard decides.
export const permissionGuard: CanActivateFn = (route) => {
  const tokenStore = inject(TokenStore);
  const router = inject(Router);
  const required = requiredPermission(route);

  return (
    (required !== undefined && (tokenStore.claims()?.permissions.includes(required) ?? false)) ||
    router.createUrlTree(['/dashboard'])
  );
};
