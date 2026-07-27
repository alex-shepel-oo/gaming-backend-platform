import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { IdentityAuthEndpoints, TokenStore } from 'shared';
import { AdminShell } from './admin-shell';

function fakeAccessToken(
  scope: string,
  permissions: string[] = [],
  role: string | null = null,
  gameId: string | null = null,
): string {
  const payload = {
    sub: 'user-1',
    email: 'admin@example.com',
    name: 'Admin One',
    scope,
    perms: permissions,
    role,
    game_id: gameId,
  };

  return `header.${btoa(JSON.stringify(payload))}.signature`;
}

@Component({ selector: 'test-stub', template: '' })
class RouteStub {}

describe('AdminShell', () => {
  let httpMock: HttpTestingController;
  let tokenStore: TokenStore;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AdminShell],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // Real route target -- see admin-login.spec.ts for why [] isn't safe here.
        provideRouter([{ path: 'login', component: RouteStub }]),
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    tokenStore = TestBed.inject(TokenStore);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('renders the signed-in user email and scope in the toolbar', () => {
    tokenStore.set(fakeAccessToken('platform'));

    const fixture = TestBed.createComponent(AdminShell);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('admin@example.com');
    expect(text).toContain('platform');
  });

  it('logging out calls the shared logout endpoint, then navigates to /login', () => {
    tokenStore.set(fakeAccessToken('platform'));
    const navigateSpy = vi.spyOn(router, 'navigateByUrl');

    const fixture = TestBed.createComponent(AdminShell);
    fixture.detectChanges();

    (Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Log out'),
    ) as HTMLButtonElement).click();

    httpMock.expectOne(IdentityAuthEndpoints.logout).flush(null);

    expect(navigateSpy).toHaveBeenCalledWith('/login');
  });

  it('hides the Games and Roles nav links when the caller holds neither permission', () => {
    tokenStore.set(fakeAccessToken('platform'));

    const fixture = TestBed.createComponent(AdminShell);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('Games');
    expect(text).not.toContain('Roles');
  });

  it('shows the Games nav link only when the caller holds platform.games.manage', () => {
    tokenStore.set(fakeAccessToken('platform', ['platform.games.manage']));

    const fixture = TestBed.createComponent(AdminShell);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Games');
    expect(text).not.toContain('Roles');
  });

  it('shows the Roles nav link only when the caller holds platform.roles.manage', () => {
    tokenStore.set(fakeAccessToken('platform', ['platform.roles.manage']));

    const fixture = TestBed.createComponent(AdminShell);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Roles');
    expect(text).not.toContain('Games');
  });

  it('hides the My Game nav link for a game-scoped caller without game.metadata.edit', () => {
    tokenStore.set(fakeAccessToken('game', [], null, 'game-1'));

    const fixture = TestBed.createComponent(AdminShell);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('My Game');
  });

  it('hides the My Game nav link for a platform-wide caller who happens to hold game.metadata.edit', () => {
    tokenStore.set(fakeAccessToken('platform', ['game.metadata.edit']));

    const fixture = TestBed.createComponent(AdminShell);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('My Game');
  });

  it('shows the My Game nav link for a game-scoped caller holding game.metadata.edit', () => {
    tokenStore.set(fakeAccessToken('game', ['game.metadata.edit'], null, 'game-1'));

    const fixture = TestBed.createComponent(AdminShell);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('My Game');
  });

  it('hides the Users nav link for a caller with no role claim', () => {
    tokenStore.set(fakeAccessToken('platform'));

    const fixture = TestBed.createComponent(AdminShell);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('Users');
  });

  it('shows the Users nav link for a Moderator', () => {
    tokenStore.set(fakeAccessToken('platform', [], 'Moderator'));

    const fixture = TestBed.createComponent(AdminShell);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Users');
  });

  it('shows the Users nav link for an Admin', () => {
    tokenStore.set(fakeAccessToken('platform', [], 'Admin'));

    const fixture = TestBed.createComponent(AdminShell);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Users');
  });
});
