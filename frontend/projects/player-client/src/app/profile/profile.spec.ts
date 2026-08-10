import { DatePipe } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import {
  CurrencyScope,
  EconomyEndpoints,
  GameSelectionService,
  IdentityAuthEndpoints,
  IdentityProfileEndpoints,
  NotificationHubService,
  ProfileService,
  UserProfile,
  WalletService,
} from 'shared';
import { Profile } from './profile';

const datePipe = new DatePipe('en-US');

describe('Profile', () => {
  let httpMock: HttpTestingController;
  let notificationHub: { connect: ReturnType<typeof vi.fn>; disconnect: ReturnType<typeof vi.fn> };

  const profile: UserProfile = {
    id: 'user-1',
    email: 'player@example.com',
    displayName: 'Player One',
    gameId: null,
    role: 'Player',
    createdAt: '2026-01-15T00:00:00Z',
    avatarUrl: null,
    lastLoginAt: '2026-07-20T12:00:00Z',
  };

  beforeEach(() => {
    notificationHub = { connect: vi.fn(), disconnect: vi.fn() };

    TestBed.configureTestingModule({
      imports: [Profile],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: NotificationHubService, useValue: notificationHub },
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  // The constructor fetches balances (for the Wallet Balance card) alongside
  // the profile itself; every test needs to flush both regardless of which
  // one it actually cares about.
  function createAndFlushBalances(balancesResponse: unknown[] = []): ComponentFixture<Profile> {
    const fixture = TestBed.createComponent(Profile);
    httpMock.expectOne((req) => req.url === EconomyEndpoints.balances).flush(balancesResponse);

    return fixture;
  }

  it('shows a loading state before the profile request resolves', () => {
    const fixture = createAndFlushBalances();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Loading profile');

    httpMock.expectOne((req) => req.url === IdentityProfileEndpoints.me).flush(profile);
  });

  it('shows the real email, display name, role, join date and last login once loaded', () => {
    const fixture = createAndFlushBalances();
    fixture.detectChanges();

    httpMock.expectOne((req) => req.url === IdentityProfileEndpoints.me).flush(profile);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Player One');
    expect(text).toContain('player@example.com');
    expect(text).toContain('Player');
    expect(text).toContain(datePipe.transform(profile.createdAt)!);
    expect(text).toContain(datePipe.transform(profile.lastLoginAt)!);
  });

  it('shows an absent-state note for last login when the user has never logged in', () => {
    const fixture = createAndFlushBalances();
    fixture.detectChanges();

    httpMock.expectOne((req) => req.url === IdentityProfileEndpoints.me).flush({ ...profile, lastLoginAt: null });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No login recorded yet');
  });

  it('shows an error message when the profile request fails', () => {
    const fixture = createAndFlushBalances();
    fixture.detectChanges();

    httpMock.expectOne((req) => req.url === IdentityProfileEndpoints.me).flush(null, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain("couldn't read your account details");
  });

  it('shows the platform balance in the Wallet Balance card', () => {
    const fixture = createAndFlushBalances([
      { currencyId: 'platform-1', currencyCode: 'PLATFORM_CREDITS', scope: CurrencyScope.Platform, gameId: null, amount: 500 },
    ]);
    fixture.detectChanges();

    httpMock.expectOne((req) => req.url === IdentityProfileEndpoints.me).flush(profile);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('500');
    expect(text).toContain('PLATFORM_CREDITS');
  });

  it('submits the edit form and calls updateMe with the entered display name', () => {
    const fixture = createAndFlushBalances();
    fixture.detectChanges();

    httpMock.expectOne((req) => req.url === IdentityProfileEndpoints.me && req.method === 'GET').flush(profile);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const displayNameInput = element.querySelector('form.profile-edit-form input') as HTMLInputElement;

    displayNameInput.value = 'New Name';
    displayNameInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (element.querySelector('form.profile-edit-form') as HTMLFormElement).dispatchEvent(
      new Event('submit', { cancelable: true }),
    );

    const req = httpMock.expectOne((r) => r.url === IdentityProfileEndpoints.me && r.method === 'PATCH');
    expect(req.request.body).toEqual({ displayName: 'New Name' });

    req.flush({ ...profile, displayName: 'New Name' });
    fixture.detectChanges();

    const text = element.textContent ?? '';
    expect(text).toContain('Profile updated.');
    expect(text).toContain('New Name');
  });

  it('reverts unsaved edits back to the loaded profile when Cancel is clicked', () => {
    const fixture = createAndFlushBalances();
    fixture.detectChanges();

    httpMock.expectOne((req) => req.url === IdentityProfileEndpoints.me).flush(profile);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const displayNameInput = element.querySelector('form.profile-edit-form input') as HTMLInputElement;

    displayNameInput.value = 'Someone Else';
    displayNameInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(displayNameInput.value).toBe('Someone Else');

    const cancelButton = Array.from(element.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Cancel'),
    )!;
    cancelButton.dispatchEvent(new Event('click'));
    fixture.detectChanges();

    expect(displayNameInput.value).toBe('Player One');
  });

  it('logs out, clears the selected game and balances, and redirects to Login', () => {
    const fixture = createAndFlushBalances([
      { currencyId: 'platform-1', currencyCode: 'PLATFORM_CREDITS', scope: CurrencyScope.Platform, gameId: null, amount: 500 },
    ]);
    httpMock.expectOne((req) => req.url === IdentityProfileEndpoints.me).flush(profile);
    fixture.detectChanges();

    const gameSelection = TestBed.inject(GameSelectionService);
    const walletService = TestBed.inject(WalletService);
    const profileService = TestBed.inject(ProfileService);
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigateByUrl');
    gameSelection.select({ id: 'game-1', slug: 'demo-shooter', name: 'Demo Shooter', description: null, iconUrl: null });

    const element = fixture.nativeElement as HTMLElement;
    const logoutButton = Array.from(element.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Log out'),
    )!;
    logoutButton.dispatchEvent(new Event('click'));

    httpMock.expectOne(IdentityAuthEndpoints.logout).flush(null);

    expect(gameSelection.selected()).toBeNull();
    expect(walletService.balances()).toBeNull();
    expect(profileService.profile()).toBeNull();
    expect(notificationHub.disconnect).toHaveBeenCalled();
    expect(navigateSpy).toHaveBeenCalledWith('/login');
  });
});
