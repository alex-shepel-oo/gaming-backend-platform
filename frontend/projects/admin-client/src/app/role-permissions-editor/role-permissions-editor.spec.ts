import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { IdentityGameEndpoints, IdentityRoleEndpoints, TokenStore } from 'shared';
import { RolePermissionsEditor } from './role-permissions-editor';

function fakeAccessToken(permissions: string[]): string {
  const payload = { sub: 'user-1', email: 'admin@example.com', name: 'Admin One', scope: 'platform', perms: permissions };

  return `header.${btoa(JSON.stringify(payload))}.signature`;
}

function checkboxLabelled(fixture: ReturnType<typeof TestBed.createComponent>, label: string): HTMLElement {
  return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('mat-checkbox')).find((element) =>
    element.textContent?.trim() === label,
  ) as HTMLElement;
}

describe('RolePermissionsEditor', () => {
  let httpMock: HttpTestingController;
  let tokenStore: TokenStore;

  const catalog = ['platform.games.manage', 'platform.roles.manage'];

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RolePermissionsEditor],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    httpMock = TestBed.inject(HttpTestingController);
    tokenStore = TestBed.inject(TokenStore);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('loads the permission catalog and the current role grants, disabling permissions the caller does not hold', () => {
    tokenStore.set(fakeAccessToken(['platform.games.manage']));

    const fixture = TestBed.createComponent(RolePermissionsEditor);

    httpMock.expectOne(IdentityRoleEndpoints.permissionCatalog).flush(catalog);
    httpMock.expectOne(IdentityGameEndpoints.allGames).flush([]);
    httpMock.expectOne(IdentityRoleEndpoints.rolePermissions('Player')).flush(['platform.games.manage']);
    fixture.detectChanges();

    const grantedCheckbox = checkboxLabelled(fixture, 'platform.games.manage').querySelector('input') as HTMLInputElement;
    const ungrantedCheckbox = checkboxLabelled(fixture, 'platform.roles.manage').querySelector('input') as HTMLInputElement;

    expect(grantedCheckbox.checked).toBe(true);
    expect(grantedCheckbox.disabled).toBe(false);
    expect(ungrantedCheckbox.disabled).toBe(true);
  });

  it('changing the game selector reloads grants scoped to that game', () => {
    tokenStore.set(fakeAccessToken(catalog));

    const fixture = TestBed.createComponent(RolePermissionsEditor);

    httpMock.expectOne(IdentityRoleEndpoints.permissionCatalog).flush(catalog);
    httpMock
      .expectOne(IdentityGameEndpoints.allGames)
      .flush([{ id: 'game-1', slug: 'space-invaders', name: 'Space Invaders', isActive: true, createdAt: '2026-01-01T00:00:00Z' }]);
    httpMock.expectOne(IdentityRoleEndpoints.rolePermissions('Player')).flush([]);
    fixture.detectChanges();

    fixture.componentInstance['onGameOptionChange']({ value: 'game-1' } as never);

    const request = httpMock.expectOne(IdentityRoleEndpoints.rolePermissions('Player', 'game-1'));
    request.flush(['game.metadata.edit']);
  });

  it('saving puts the currently granted permission set for the selected role and scope', () => {
    tokenStore.set(fakeAccessToken(catalog));

    const fixture = TestBed.createComponent(RolePermissionsEditor);

    httpMock.expectOne(IdentityRoleEndpoints.permissionCatalog).flush(catalog);
    httpMock.expectOne(IdentityGameEndpoints.allGames).flush([]);
    httpMock.expectOne(IdentityRoleEndpoints.rolePermissions('Player')).flush(['platform.games.manage']);
    fixture.detectChanges();

    const saveButton = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Save'),
    ) as HTMLButtonElement;
    saveButton.click();

    const request = httpMock.expectOne(IdentityRoleEndpoints.rolePermissions('Player'));
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ permissions: ['platform.games.manage'] });
    request.flush(['platform.games.manage']);
  });
});
