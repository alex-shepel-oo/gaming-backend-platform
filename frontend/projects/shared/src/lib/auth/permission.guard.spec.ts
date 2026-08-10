import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, provideRouter, Router, RouterStateSnapshot } from '@angular/router';
import { permissionGuard } from './permission.guard';
import { TokenStore } from './token-store';

function fakeAccessToken(permissions: string[]): string {
  const payload = { sub: 'user-1', email: 'admin@example.com', name: 'Admin One', scope: 'platform', perms: permissions };

  return `header.${btoa(JSON.stringify(payload))}.signature`;
}

function routeRequiring(permission: string): ActivatedRouteSnapshot {
  return { data: { permission } } as unknown as ActivatedRouteSnapshot;
}

// permissionGuard only decides what admin-client renders, it is UX, not
// the security boundary. The backend rejects an unauthorized request on its
// own regardless of what this guard decides (ADR-0012).
describe('permissionGuard', () => {
  let tokenStore: TokenStore;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });

    tokenStore = TestBed.inject(TokenStore);
    router = TestBed.inject(Router);
  });

  function run(route: ActivatedRouteSnapshot) {
    return TestBed.runInInjectionContext(() => permissionGuard(route, {} as RouterStateSnapshot));
  }

  it('redirects to /users when the caller lacks the required permission', () => {
    tokenStore.set(fakeAccessToken(['platform.roles.manage']));

    const result = run(routeRequiring('platform.games.manage'));

    expect(result).toEqual(router.createUrlTree(['/users']));
  });

  it('redirects to /users when there is no session at all', () => {
    const result = run(routeRequiring('platform.games.manage'));

    expect(result).toEqual(router.createUrlTree(['/users']));
  });

  it('allows navigation when the permissions claim includes the required permission', () => {
    tokenStore.set(fakeAccessToken(['platform.games.manage']));

    expect(run(routeRequiring('platform.games.manage'))).toBe(true);
  });
});
