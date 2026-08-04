import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CurrencyScope, EconomyEndpoints, IdentityGameEndpoints, TransactionType } from 'shared';
import { Wallet } from './wallet';

describe('Wallet', () => {
  let httpMock: HttpTestingController;

  const balances = [
    {
      currencyId: 'platform-1',
      currencyCode: 'PLATFORM_CREDITS',
      scope: CurrencyScope.Platform,
      gameId: null,
      amount: 500,
      iconUrl: 'https://placehold.co/64x64?text=Credits',
    },
    {
      currencyId: 'game-1-currency',
      currencyCode: 'SHOOTER_GOLD',
      scope: CurrencyScope.Game,
      gameId: 'game-1',
      amount: 10,
      iconUrl: null,
    },
    {
      currencyId: 'game-2-currency',
      currencyCode: 'PUZZLE_GEMS',
      scope: CurrencyScope.Game,
      gameId: 'game-2',
      amount: 3,
      iconUrl: null,
    },
  ];

  const currencyCatalog = [
    { id: 'platform-1', code: 'PLATFORM_CREDITS', displayName: 'Platform Credits', scope: CurrencyScope.Platform, gameId: null, decimals: 2, iconUrl: null },
    { id: 'game-1-currency', code: 'SHOOTER_GOLD', displayName: 'Shooter Gold', scope: CurrencyScope.Game, gameId: 'game-1', decimals: 2, iconUrl: null },
    { id: 'game-2-currency', code: 'PUZZLE_GEMS', displayName: 'Puzzle Gems', scope: CurrencyScope.Game, gameId: 'game-2', decimals: 2, iconUrl: null },
  ];

  const publicGames = [
    { id: 'game-1', slug: 'demo-shooter', name: 'Demo Shooter', description: null, iconUrl: null },
    { id: 'game-2', slug: 'demo-puzzle', name: 'Demo Puzzle', description: null, iconUrl: null },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Wallet],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function flushCurrencies(): void {
    httpMock.expectOne((req) => req.url === EconomyEndpoints.currencies).flush(currencyCatalog);
  }

  function flushGames(): void {
    httpMock.expectOne((req) => req.url === IdentityGameEndpoints.publicGames).flush(publicGames);
  }

  function createWallet(
    balancesResponse: unknown[] = balances,
    transactionsResponse: Record<string, unknown> = { items: [], page: 1, pageSize: 20, totalCount: 0 },
  ): ComponentFixture<Wallet> {
    const fixture = TestBed.createComponent(Wallet);

    httpMock.expectOne((req) => req.url === EconomyEndpoints.balances).flush(balancesResponse);
    flushCurrencies();
    flushGames();
    httpMock.expectOne((req) => req.url === EconomyEndpoints.transactions).flush(transactionsResponse);
    fixture.detectChanges();

    return fixture;
  }

  function nextPageButton(fixture: { nativeElement: unknown }): HTMLButtonElement {
    const element = fixture.nativeElement as HTMLElement;

    return Array.from(element.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Next page'),
    ) as HTMLButtonElement;
  }

  it('renders platform balances and one card per game, plus the transaction history', () => {
    const fixture = createWallet(balances, {
      items: [
        {
          id: 'tx-1',
          currencyId: 'platform-1',
          amount: 100,
          transactionType: TransactionType.Grant,
          reason: null,
          createdAt: '2026-07-20T00:00:00Z',
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    });

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('PLATFORM_CREDITS');
    expect(text).toContain('SHOOTER_GOLD');
    expect(text).toContain('PUZZLE_GEMS');
    expect(text).toContain('Demo Shooter');
    expect(text).toContain('Demo Puzzle');
    expect(text).not.toContain('platform-1');
    expect(text).toContain('Grant');
  });

  it('shows a currency icon when iconUrl is present, and no image at all when it is null', () => {
    const fixture = createWallet(balances, { items: [], page: 1, pageSize: 20, totalCount: 0 });

    const images = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('img.currency-icon'));
    expect(images).toHaveLength(1);
    expect(images[0].getAttribute('src')).toBe('https://placehold.co/64x64?text=Credits');
    expect(images[0].getAttribute('alt')).toBe('PLATFORM_CREDITS');
  });

  it('shows an empty state when there are no balances or transactions yet', () => {
    const fixture = createWallet([]);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No balances yet.');
    expect(text).toContain('No transactions yet.');
  });

  it('shows an error state when balances fail to load', () => {
    const fixture = TestBed.createComponent(Wallet);

    httpMock
      .expectOne((req) => req.url === EconomyEndpoints.balances)
      .flush({ status: 500, title: 'Server error' }, { status: 500, statusText: 'Internal Server Error' });
    flushCurrencies();
    flushGames();
    httpMock
      .expectOne((req) => req.url === EconomyEndpoints.transactions)
      .flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain("couldn't load your balances");
  });

  it('requests the next page with an incremented page number', () => {
    const fixture = TestBed.createComponent(Wallet);

    httpMock.expectOne((req) => req.url === EconomyEndpoints.balances).flush([]);
    flushCurrencies();
    flushGames();
    httpMock
      .expectOne((req) => req.url === EconomyEndpoints.transactions && req.params.get('page') === '1')
      .flush({ items: [], page: 1, pageSize: 20, totalCount: 40 });
    fixture.detectChanges();

    nextPageButton(fixture).dispatchEvent(new Event('click'));

    httpMock
      .expectOne((req) => req.url === EconomyEndpoints.transactions && req.params.get('page') === '2')
      .flush({ items: [], page: 2, pageSize: 20, totalCount: 40 });
  });

  it('disables both Previous and Next when everything fits on one page', () => {
    const fixture = TestBed.createComponent(Wallet);

    httpMock.expectOne((req) => req.url === EconomyEndpoints.balances).flush([]);
    flushCurrencies();
    flushGames();
    httpMock
      .expectOne((req) => req.url === EconomyEndpoints.transactions && req.params.get('page') === '1')
      .flush({ items: [], page: 1, pageSize: 20, totalCount: 10 });
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const previousButton = Array.from(element.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Previous'),
    ) as HTMLButtonElement;

    expect(previousButton.disabled).toBe(true);
    expect(nextPageButton(fixture).disabled).toBe(true);
  });

  it('shows the currency code, not the raw currency id, in the transaction history table', () => {
    const fixture = createWallet(balances, {
      items: [
        {
          id: 'tx-1',
          currencyId: 'game-1-currency',
          amount: 5,
          transactionType: TransactionType.Spend,
          reason: null,
          createdAt: '2026-07-20T00:00:00Z',
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    });

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('SHOOTER_GOLD');
    expect(text).not.toContain('game-1-currency');
  });
});
