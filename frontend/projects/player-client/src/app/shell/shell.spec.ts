import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import {
  CurrencyScope,
  EconomyEndpoints,
  GameSelectionService,
  IdentityProfileEndpoints,
  NotificationHubService,
  ProfileService,
  TokenStore,
  UserProfile,
  WalletService,
} from 'shared';
import { Shell } from './shell';

function base64UrlEncode(value: string): string {
  const bytes = new TextEncoder().encode(value);
  let binary = '';
  bytes.forEach((byte) => {
    binary += String.fromCharCode(byte);
  });

  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function buildFakeToken(payload: Record<string, unknown>): string {
  const header = base64UrlEncode(JSON.stringify({ alg: 'none' }));
  const body = base64UrlEncode(JSON.stringify(payload));

  return `${header}.${body}.signature`;
}

describe('Shell', () => {
  let httpMock: HttpTestingController;
  let gameSelection: GameSelectionService;
  let tokenStore: TokenStore;
  let walletService: WalletService;
  let profileService: ProfileService;
  let notificationHub: { connect: ReturnType<typeof vi.fn>; disconnect: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    notificationHub = { connect: vi.fn(), disconnect: vi.fn() };

    // ThemeService (injected by Shell) reads window.matchMedia on construction
    // to pick an initial mode, which jsdom doesn't implement.
    vi.stubGlobal(
      'matchMedia',
      vi.fn().mockImplementation((query: string) => ({
        matches: false,
        media: query,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      })),
    );

    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: NotificationHubService, useValue: notificationHub },
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    gameSelection = TestBed.inject(GameSelectionService);
    tokenStore = TestBed.inject(TokenStore);
    walletService = TestBed.inject(WalletService);
    profileService = TestBed.inject(ProfileService);
  });

  afterEach(() => {
    httpMock.verify();
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  function createAndFlushBalances(balances: unknown[] = [], profile: UserProfile | null = null) {
    const fixture = TestBed.createComponent(Shell);
    httpMock.expectOne((req) => req.url === EconomyEndpoints.balances).flush(balances);
    httpMock.expectOne((req) => req.url === IdentityProfileEndpoints.me).flush(profile);
    fixture.detectChanges();

    return fixture;
  }

  it('renders navigation links to the three authenticated screens', () => {
    const fixture = createAndFlushBalances();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Games');
    expect(text).toContain('Wallet');
    expect(text).toContain('Convert');
  });

  it('shows the selected game once one has been picked', () => {
    const fixture = createAndFlushBalances();

    let text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('Demo Shooter');

    gameSelection.select({ id: 'game-1', slug: 'demo-shooter', name: 'Demo Shooter', description: null, iconUrl: null });
    fixture.detectChanges();

    text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Demo Shooter');
  });

  it('shows the platform balance amount once it loads, without the redundant currency code text', () => {
    const fixture = createAndFlushBalances([
      { currencyId: 'platform-1', currencyCode: 'PLATFORM_CREDITS', scope: CurrencyScope.Platform, gameId: null, amount: 500 },
      { currencyId: 'game-1', currencyCode: 'SHOOTER_GOLD', scope: CurrencyScope.Game, gameId: 'game-1', amount: 10 },
    ]);

    const element = fixture.nativeElement as HTMLElement;
    const text = element.textContent ?? '';
    expect(text).toContain('500');
    expect(text).not.toContain('10');
    expect(text).not.toContain('PLATFORM_CREDITS');
    expect(text).not.toContain('SHOOTER_GOLD');

    // The compact toolbar drops the visible code text, but keeps the
    // existing title attribute for context.
    const balanceSpan = element.querySelector('.shell-balance') as HTMLElement;
    expect(balanceSpan.getAttribute('title')).toBe('Platform balance');
  });

  it('still syncs the color-scheme attribute on load even without a visible toggle button', () => {
    // The toggle button was moved off the header, but ThemeService's own
    // mode-detection/persistence must keep running regardless. This only
    // happens if something still injects it, which Shell does purely for
    // that side effect now (see the comment on its `theme` field).
    document.documentElement.style.colorScheme = '';

    createAndFlushBalances();

    expect(document.documentElement.style.colorScheme).toMatch(/^(light|dark)$/);
  });

  it('shows an avatar linking to the profile once the user is known', () => {
    tokenStore.set(
      buildFakeToken({ sub: 'user-1', email: 'player@example.com', name: 'Player One', role: 'Player', scope: 'game' }),
    );

    const fixture = createAndFlushBalances();

    const link = (fixture.nativeElement as HTMLElement).querySelector('a[href="/profile"]');
    expect(link).toBeTruthy();
    expect(link!.textContent).toContain('PO');
  });

  it('refreshes the profile on construction and shows the fetched avatar image', () => {
    tokenStore.set(
      buildFakeToken({ sub: 'user-1', email: 'player@example.com', name: 'Player One', role: 'Player', scope: 'game' }),
    );

    const fixture = createAndFlushBalances([], {
      id: 'user-1',
      email: 'player@example.com',
      displayName: 'Player One',
      gameId: null,
      role: 'Player',
      createdAt: '2026-01-01T00:00:00Z',
      avatarUrl: 'https://example.com/avatar.png',
      lastLoginAt: null,
    });

    expect(profileService.profile()?.avatarUrl).toBe('https://example.com/avatar.png');

    const img = (fixture.nativeElement as HTMLElement).querySelector('a[href="/profile"] img');
    expect(img?.getAttribute('src')).toBe('https://example.com/avatar.png');
  });
});
