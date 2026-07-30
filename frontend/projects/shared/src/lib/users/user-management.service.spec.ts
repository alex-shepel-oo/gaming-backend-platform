import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { IdentityUserEndpoints } from './identity-user-endpoints';
import { UserManagementService } from './user-management.service';

describe('UserManagementService', () => {
  let httpMock: HttpTestingController;
  let service: UserManagementService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    httpMock = TestBed.inject(HttpTestingController);
    service = TestBed.inject(UserManagementService);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('listUsers requests a page of the users list, forwarding search when given', () => {
    const page = { items: [], page: 1, pageSize: 20, totalCount: 0 };
    let result: unknown;
    service.listUsers('alex', 1, 20).subscribe((value) => (result = value));

    const request = httpMock.expectOne(IdentityUserEndpoints.list('alex', 1, 20));
    expect(request.request.method).toBe('GET');
    request.flush(page);

    expect(result).toEqual(page);
  });

  it('listUsers carries lastLoginAt through for each returned user, including a never-logged-in null', () => {
    const page = {
      items: [
        {
          id: 'user-1',
          email: 'one@example.com',
          displayName: 'User One',
          role: 'Player',
          createdAt: '2026-01-01T00:00:00Z',
          lastLoginAt: '2026-07-20T08:00:00Z',
          gameId: 'game-1',
          gameSlug: 'demo-shooter',
        },
        {
          id: 'user-2',
          email: 'two@example.com',
          displayName: 'User Two',
          role: 'Player',
          createdAt: '2026-01-02T00:00:00Z',
          lastLoginAt: null,
          gameId: null,
          gameSlug: null,
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 2,
    };
    let result: unknown;
    service.listUsers(undefined, 1, 20).subscribe((value) => (result = value));

    const request = httpMock.expectOne(IdentityUserEndpoints.list(undefined, 1, 20));
    request.flush(page);

    expect(result).toEqual(page);
  });

  it('listUsers omits the search param when none is given', () => {
    service.listUsers(undefined, 1, 20).subscribe();

    const request = httpMock.expectOne(IdentityUserEndpoints.list(undefined, 1, 20));
    expect(request.request.url).not.toContain('search');
    request.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
  });

  it('getUser requests the full detail record for a single user', () => {
    const detail = {
      id: 'user-1',
      email: 'player@example.com',
      displayName: 'Player One',
      gameId: null,
      gameSlug: null,
      role: 'Moderator',
      createdAt: '2026-01-01T00:00:00Z',
    };
    let result: unknown;
    service.getUser('user-1').subscribe((value) => (result = value));

    const request = httpMock.expectOne(IdentityUserEndpoints.detail('user-1'));
    expect(request.request.method).toBe('GET');
    request.flush(detail);

    expect(result).toEqual(detail);
  });

  it('getUser forwards a gameId so a cross-game row resolves against its own game', () => {
    const detail = {
      id: 'user-1',
      email: 'player@example.com',
      displayName: 'Player One',
      gameId: 'game-1',
      gameSlug: 'demo-shooter',
      role: 'Player',
      createdAt: '2026-01-01T00:00:00Z',
    };
    let result: unknown;
    service.getUser('user-1', 'game-1').subscribe((value) => (result = value));

    const request = httpMock.expectOne(IdentityUserEndpoints.detail('user-1', 'game-1'));
    expect(request.request.method).toBe('GET');
    request.flush(detail);

    expect(result).toEqual(detail);
  });

  it('assignRole patches the role endpoint and returns the UserRoleDto-shaped assignment, not a full user', () => {
    const assignment = { userId: 'user-1', gameId: 'game-1', role: 'Moderator', grantedAt: '2026-01-01T00:00:00Z' };
    let result: unknown;
    service.assignRole('user-1', 'game-1', 'Moderator').subscribe((value) => (result = value));

    const request = httpMock.expectOne(IdentityUserEndpoints.role('user-1'));
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ gameId: 'game-1', role: 'Moderator' });
    request.flush(assignment);

    expect(result).toEqual(assignment);
  });

  it('assignRole sends a null gameId for a platform-wide grant', () => {
    service.assignRole('user-1', undefined, 'Admin').subscribe();

    const request = httpMock.expectOne(IdentityUserEndpoints.role('user-1'));
    expect(request.request.body).toEqual({ gameId: null, role: 'Admin' });
    request.flush({ userId: 'user-1', gameId: null, role: 'Admin', grantedAt: '2026-01-01T00:00:00Z' });
  });

  it('revokeSessions posts to the revoke-sessions endpoint with no body', () => {
    let completed = false;
    service.revokeSessions('user-1', 'game-1').subscribe(() => (completed = true));

    const request = httpMock.expectOne(IdentityUserEndpoints.revokeSessions('user-1', 'game-1'));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBeNull();
    request.flush(null);

    expect(completed).toBe(true);
  });
});
