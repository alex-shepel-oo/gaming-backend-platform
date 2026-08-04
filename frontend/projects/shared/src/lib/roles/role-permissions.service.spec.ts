import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { IdentityRoleEndpoints } from './identity-role-endpoints';
import { RolePermissionsService } from './role-permissions.service';

describe('RolePermissionsService', () => {
  let httpMock: HttpTestingController;
  let service: RolePermissionsService;

  const catalog = ['platform.games.manage', 'platform.roles.manage', 'game.metadata.edit'];

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    httpMock = TestBed.inject(HttpTestingController);
    service = TestBed.inject(RolePermissionsService);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('getPermissionCatalog requests the full assignable permission catalog', () => {
    let result: unknown;
    service.getPermissionCatalog().subscribe((permissions) => (result = permissions));

    const request = httpMock.expectOne(IdentityRoleEndpoints.permissionCatalog);
    expect(request.request.method).toBe('GET');
    request.flush(catalog);

    expect(result).toEqual(catalog);
  });

  it('getRolePermissions requests the platform-wide grant when no gameId is given', () => {
    let result: unknown;
    service.getRolePermissions('Moderator').subscribe((permissions) => (result = permissions));

    const request = httpMock.expectOne(IdentityRoleEndpoints.rolePermissions('Moderator'));
    expect(request.request.method).toBe('GET');
    expect(request.request.url).not.toContain('?');
    request.flush(['platform.games.manage']);

    expect(result).toEqual(['platform.games.manage']);
  });

  it('getRolePermissions requests the per-game template when a gameId is given', () => {
    let result: unknown;
    service.getRolePermissions('Moderator', 'game-1').subscribe((permissions) => (result = permissions));

    const request = httpMock.expectOne(IdentityRoleEndpoints.rolePermissions('Moderator', 'game-1'));
    expect(request.request.method).toBe('GET');
    request.flush(['game.metadata.edit']);

    expect(result).toEqual(['game.metadata.edit']);
  });

  it('updateRolePermissions puts the new permission set for the role', () => {
    let result: unknown;
    service.updateRolePermissions('Admin', ['platform.games.manage'], 'game-1').subscribe((permissions) => (result = permissions));

    const request = httpMock.expectOne(IdentityRoleEndpoints.rolePermissions('Admin', 'game-1'));
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ permissions: ['platform.games.manage'] });
    request.flush(['platform.games.manage']);

    expect(result).toEqual(['platform.games.manage']);
  });
});
