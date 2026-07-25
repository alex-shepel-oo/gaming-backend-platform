import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import {
  CurrencyScope,
  EconomyEndpoints,
  GameSelectionService,
  IdentityAuthEndpoints,
  NotificationHubService,
  TokenStore,
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
  let router: Router;
  let gameSelection: GameSelectionService;
  let tokenStore: TokenStore;
  let walletService: WalletService;
  let notificationHub: { connect: ReturnType<typeof vi.fn>; disconnect: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    notificationHub = { connect: vi.fn(), disconnect: vi.fn() };

    // ThemeService (injected by Shell) reads window.matchMedia on construction
    // to pick an initial mode -- jsdom doesn't implement it.
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
    router = TestBed.inject(Router);
    gameSelection = TestBed.inject(GameSelectionService);
    tokenStore = TestBed.inject(TokenStore);
    walletService = TestBed.inject(WalletService);
  });

  afterEach(() => {
    httpMock.verify();
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  function createAndFlushBalances(balances: unknown[] = []) {
    const fixture = TestBed.createComponent(Shell);
    httpMock.expectOne((req) => req.url === EconomyEndpoints.balances).flush(balances);
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

    gameSelection.select({ id: 'game-1', slug: 'demo-shooter', name: 'Demo Shooter' });
    fixture.detectChanges();

    text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Demo Shooter');
  });

  it('shows the platform balance once it loads', () => {
    const fixture = createAndFlushBalances([
      { currencyId: 'platform-1', currencyCode: 'PLATFORM_CREDITS', scope: CurrencyScope.Platform, gameId: null, amount: 500 },
      { currencyId: 'game-1', currencyCode: 'SHOOTER_GOLD', scope: CurrencyScope.Game, gameId: 'game-1', amount: 10 },
    ]);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('500 PLATFORM_CREDITS');
    expect(text).not.toContain('10 SHOOTER_GOLD');
  });

  it('toggles the theme icon when the theme button is clicked', () => {
    const fixture = createAndFlushBalances();

    const themeButton = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find(
      (button) => button.getAttribute('aria-label')?.includes('theme'),
    )!;
    const initialLabel = themeButton.getAttribute('aria-label');

    themeButton.dispatchEvent(new Event('click'));
    fixture.detectChanges();

    expect(themeButton.getAttribute('aria-label')).not.toBe(initialLabel);
  });

  it('shows an avatar linking to the profile once the user is known', () => {
    tokenStore.set(
      buildFakeToken({ sub: 'user-1', email: 'player@example.com', name: 'Player One', role: 'Player' }),
    );

    const fixture = createAndFlushBalances();

    const link = (fixture.nativeElement as HTMLElement).querySelector('a[href="/profile"]');
    expect(link).toBeTruthy();
    expect(link!.textContent).toContain('PO');
  });

  it('logs out, clears the selected game and balances, and redirects to Login', () => {
    const navigateSpy = vi.spyOn(router, 'navigateByUrl');
    gameSelection.select({ id: 'game-1', slug: 'demo-shooter', name: 'Demo Shooter' });

    const fixture = createAndFlushBalances([
      { currencyId: 'platform-1', currencyCode: 'PLATFORM_CREDITS', scope: CurrencyScope.Platform, gameId: null, amount: 500 },
    ]);

    const buttons = (fixture.nativeElement as HTMLElement).querySelectorAll('button');
    const logoutButton = Array.from(buttons).find((button) => button.textContent?.includes('Log out'));
    logoutButton!.dispatchEvent(new Event('click'));

    httpMock.expectOne(IdentityAuthEndpoints.logout).flush(null);

    expect(gameSelection.selected()).toBeNull();
    expect(walletService.balances()).toBeNull();
    expect(notificationHub.disconnect).toHaveBeenCalled();
    expect(navigateSpy).toHaveBeenCalledWith('/login');
  });
});
