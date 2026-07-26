import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { IdentityAuthEndpoints, TokenStore } from 'shared';
import { AdminShell } from './admin-shell';

function fakeAccessToken(scope: string, permissions: string[] = []): string {
  const payload = { sub: 'user-1', email: 'admin@example.com', name: 'Admin One', scope, perms: permissions };

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
});
