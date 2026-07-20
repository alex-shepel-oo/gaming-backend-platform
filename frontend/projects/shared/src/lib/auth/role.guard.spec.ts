import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, provideRouter, Router, RouterStateSnapshot } from '@angular/router';
import { roleGuard } from './role.guard';
import { TokenStore } from './token-store';

function fakeAccessToken(role: string): string {
  return `header.${btoa(JSON.stringify({ role }))}.signature`;
}

function routeRequiring(...roles: string[]): ActivatedRouteSnapshot {
  return { data: { roles } } as unknown as ActivatedRouteSnapshot;
}

// roleGuard only decides what to render -- it is UX, not the security
// boundary. The backend rejects an unauthorized request on its own
// regardless of what this guard decides (ADR-0012).
describe('roleGuard', () => {
  let tokenStore: TokenStore;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });

    tokenStore = TestBed.inject(TokenStore);
    router = TestBed.inject(Router);
  });

  function run(route: ActivatedRouteSnapshot) {
    return TestBed.runInInjectionContext(() => roleGuard(route, {} as RouterStateSnapshot));
  }

  it('redirects a session with an insufficient role', () => {
    tokenStore.set(fakeAccessToken('Player'));

    const result = run(routeRequiring('Admin'));

    expect(result).toEqual(router.createUrlTree(['/login']));
  });

  it('allows navigation when the role claim satisfies the route requirement', () => {
    tokenStore.set(fakeAccessToken('Admin'));

    expect(run(routeRequiring('Admin'))).toBe(true);
  });
});
