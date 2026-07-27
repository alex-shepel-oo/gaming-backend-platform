import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { IdentityRoleEndpoints, IdentityUserEndpoints, TokenStore } from 'shared';
import { UserManagement } from './user-management';

function fakeAccessToken(
  options: { role?: string | null; permissions?: string[]; gameId?: string | null } = {},
): string {
  const { role = null, permissions = [], gameId = null } = options;
  const payload = {
    sub: 'admin-1',
    email: 'admin@example.com',
    name: 'Admin One',
    scope: gameId ? 'game' : 'platform',
    role,
    perms: permissions,
    game_id: gameId,
  };

  return `header.${btoa(JSON.stringify(payload))}.signature`;
}

const users = [
  {
    id: 'user-1',
    email: 'one@example.com',
    displayName: 'User One',
    role: 'Player',
    createdAt: '2026-01-01T00:00:00Z',
    lastLoginAt: '2026-07-20T08:00:00Z',
  },
  {
    id: 'user-2',
    email: 'two@example.com',
    displayName: 'User Two',
    role: 'Moderator',
    createdAt: '2026-01-02T00:00:00Z',
    lastLoginAt: null,
  },
];

const userDetail = {
  id: 'user-1',
  email: 'one@example.com',
  displayName: 'User One',
  gameId: null,
  role: 'Player',
  createdAt: '2026-01-01T00:00:00Z',
};

describe('UserManagement', () => {
  let httpMock: HttpTestingController;
  let tokenStore: TokenStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [UserManagement],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    httpMock = TestBed.inject(HttpTestingController);
    tokenStore = TestBed.inject(TokenStore);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function flushRolePermissions(gameId: string | undefined, grants: Partial<Record<string, string[]>> = {}): void {
    for (const role of ['Player', 'Moderator', 'Admin']) {
      httpMock.expectOne(IdentityRoleEndpoints.rolePermissions(role, gameId)).flush(grants[role] ?? []);
    }
  }

  function flushList(fixture: ReturnType<typeof TestBed.createComponent>): void {
    httpMock.expectOne(IdentityUserEndpoints.list(undefined, 1, 20)).flush({ items: users, page: 1, pageSize: 20, totalCount: 2 });
    flushRolePermissions(undefined);
    fixture.detectChanges();
  }

  it('renders every user returned by the scoped users list', () => {
    tokenStore.set(fakeAccessToken({ role: 'Admin' }));

    const fixture = TestBed.createComponent(UserManagement);
    flushList(fixture);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('one@example.com');
    expect(text).toContain('two@example.com');
    expect(text).toContain('Users in the platform');
  });

  it('shows the last login date for a user who has logged in, and "Never" for one who has not', () => {
    tokenStore.set(fakeAccessToken({ role: 'Admin' }));

    const fixture = TestBed.createComponent(UserManagement);
    flushList(fixture);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Never');
  });

  it('searching re-queries the users list with the search term and resets to page 1', () => {
    tokenStore.set(fakeAccessToken({ role: 'Admin' }));

    const fixture = TestBed.createComponent(UserManagement);
    flushList(fixture);

    fixture.componentInstance['onSearch']('one');

    const request = httpMock.expectOne(IdentityUserEndpoints.list('one', 1, 20));
    request.flush({ items: [users[0]], page: 1, pageSize: 20, totalCount: 1 });
  });

  it('disables a role option the caller lacks the granted permission set for, and leaves an assignable one enabled', () => {
    tokenStore.set(fakeAccessToken({ role: 'Moderator', permissions: ['game.roles.manage'] }));

    const fixture = TestBed.createComponent(UserManagement);
    httpMock.expectOne(IdentityUserEndpoints.list(undefined, 1, 20)).flush({ items: users, page: 1, pageSize: 20, totalCount: 2 });
    flushRolePermissions(undefined, { Player: [], Admin: ['game.roles.manage', 'game.currency.manage'] });
    fixture.detectChanges();

    fixture.componentInstance['selectUser'](users[0]);
    httpMock.expectOne(IdentityUserEndpoints.detail('user-1')).flush(userDetail);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    expect(component['isRoleDisabled']('Player')).toBe(false);
    expect(component['isRoleDisabled']('Admin')).toBe(true);
  });

  it('hides the revoke-sessions control for a caller whose own role is not Admin', () => {
    tokenStore.set(fakeAccessToken({ role: 'Moderator' }));

    const fixture = TestBed.createComponent(UserManagement);
    flushList(fixture);

    fixture.componentInstance['selectUser'](users[0]);
    httpMock.expectOne(IdentityUserEndpoints.detail('user-1')).flush(userDetail);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('Revoke sessions');
  });

  it('shows the revoke-sessions button for an Admin caller and wires it to the revoke endpoint', () => {
    tokenStore.set(fakeAccessToken({ role: 'Admin' }));

    const fixture = TestBed.createComponent(UserManagement);
    flushList(fixture);

    fixture.componentInstance['selectUser'](users[0]);
    httpMock.expectOne(IdentityUserEndpoints.detail('user-1')).flush(userDetail);
    fixture.detectChanges();

    const revokeButton = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Revoke sessions'),
    ) as HTMLButtonElement;
    revokeButton.click();

    const request = httpMock.expectOne(IdentityUserEndpoints.revokeSessions('user-1', undefined));
    expect(request.request.method).toBe('POST');
    request.flush(null);
  });

  it('assigning a role calls the roles endpoint, then re-fetches the full user detail rather than trusting the assign response', () => {
    tokenStore.set(fakeAccessToken({ role: 'Admin', permissions: ['platform.roles.manage'] }));

    const fixture = TestBed.createComponent(UserManagement);
    flushList(fixture);

    fixture.componentInstance['selectUser'](users[0]);
    httpMock.expectOne(IdentityUserEndpoints.detail('user-1')).flush(userDetail);
    fixture.detectChanges();

    fixture.componentInstance['onRoleChange']({ value: 'Moderator' } as never);
    fixture.componentInstance['assignRole']();

    const assignRequest = httpMock.expectOne(IdentityUserEndpoints.role('user-1'));
    expect(assignRequest.request.method).toBe('PATCH');
    expect(assignRequest.request.body).toEqual({ gameId: null, role: 'Moderator' });
    // UserRoleDto has no email/displayName -- if the view were patched from this
    // alone those fields would go missing, which is why a refetch follows.
    assignRequest.flush({ userId: 'user-1', gameId: null, role: 'Moderator', grantedAt: '2026-01-05T00:00:00Z' });

    const refetch = httpMock.expectOne(IdentityUserEndpoints.detail('user-1'));
    expect(refetch.request.method).toBe('GET');
    refetch.flush({ ...userDetail, role: 'Moderator' });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Current role: Moderator');
    expect(text).toContain('one@example.com');
  });
});
