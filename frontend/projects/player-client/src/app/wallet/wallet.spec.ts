import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
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
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
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

  it('excludes zero-amount balances from Game Assets, and drops a game entirely once none are left', () => {
    const fixture = createWallet([
      balances[0],
      { ...balances[1], amount: 0 },
      balances[2],
    ]);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('SHOOTER_GOLD');
    expect(text).not.toContain('Demo Shooter');
    expect(text).toContain('PUZZLE_GEMS');
    expect(text).toContain('Demo Puzzle');
  });

  it('shows a currency icon when iconUrl is present, and a fallback badge when it is null', () => {
    const fixture = createWallet(balances, { items: [], page: 1, pageSize: 20, totalCount: 0 });

    const element = fixture.nativeElement as HTMLElement;
    const images = Array.from(element.querySelectorAll('img.currency-icon'));
    expect(images).toHaveLength(1);
    expect(images[0].getAttribute('src')).toBe('https://placehold.co/64x64?text=Credits');
    expect(images[0].getAttribute('alt')).toBe('PLATFORM_CREDITS');

    // SHOOTER_GOLD and PUZZLE_GEMS both have iconUrl: null.
    expect(element.querySelectorAll('.currency-icon--fallback')).toHaveLength(2);
  });

  it('falls back to the badge when a currency icon fails to load', () => {
    const fixture = createWallet(balances, { items: [], page: 1, pageSize: 20, totalCount: 0 });

    const element = fixture.nativeElement as HTMLElement;
    const image = element.querySelector('img.currency-icon') as HTMLImageElement;
    image.dispatchEvent(new Event('error'));
    fixture.detectChanges();

    expect(element.querySelector('img.currency-icon')).toBeNull();
    expect(element.querySelectorAll('.currency-icon--fallback')).toHaveLength(3);
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

  it('formats the transaction date instead of showing the raw ISO timestamp', () => {
    const fixture = createWallet(balances, {
      items: [
        {
          id: 'tx-1',
          currencyId: 'platform-1',
          amount: 100,
          transactionType: TransactionType.Grant,
          reason: null,
          createdAt: '2026-08-07T16:17:01.731472+00:00',
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    });

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('2026-08-07T16:17:01.731472+00:00');
  });

  it('clicking a history filter re-requests with the matching types params', () => {
    const fixture = createWallet();

    const filterButton = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === 'Conversions',
    ) as HTMLButtonElement;
    filterButton.dispatchEvent(new Event('click'));

    const request = httpMock.expectOne(
      (req) => req.url === EconomyEndpoints.transactions && req.params.get('page') === '1',
    );
    expect(request.request.params.getAll('types')).toEqual([
      `${TransactionType.ConversionOut}`,
      `${TransactionType.ConversionIn}`,
    ]);
    request.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
  });

  it('combines a matching ConversionOut/ConversionIn pair into a single "Converted" row', () => {
    const fixture = createWallet(balances, {
      items: [
        {
          id: 'tx-credit',
          currencyId: 'game-1-currency',
          amount: 100,
          transactionType: TransactionType.ConversionIn,
          reason: 'conversion credit',
          createdAt: '2026-08-07T16:17:05.000000+00:00',
          idempotencyKey: 'conversion:conv-1:credit',
        },
        {
          id: 'tx-debit',
          currencyId: 'platform-1',
          amount: -1,
          transactionType: TransactionType.ConversionOut,
          reason: 'conversion debit',
          createdAt: '2026-08-07T16:17:01.000000+00:00',
          idempotencyKey: 'conversion:conv-1:debit',
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 2,
    });

    const element = fixture.nativeElement as HTMLElement;
    const rows = element.querySelectorAll('.transaction-row');
    expect(rows).toHaveLength(1);

    const text = rows[0].textContent ?? '';
    expect(text).toContain('Converted');
    expect(text).toContain('-1 PLATFORM_CREDITS');
    expect(text).toContain('+100 SHOOTER_GOLD');
  });

  it('falls back to two separate rows when only one leg of a conversion is on the page', () => {
    const fixture = createWallet(balances, {
      items: [
        {
          id: 'tx-debit-only',
          currencyId: 'platform-1',
          amount: -1,
          transactionType: TransactionType.ConversionOut,
          reason: 'conversion debit',
          createdAt: '2026-08-07T16:17:01.000000+00:00',
          idempotencyKey: 'conversion:conv-2:debit',
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    });

    const element = fixture.nativeElement as HTMLElement;
    const rows = element.querySelectorAll('.transaction-row');
    expect(rows).toHaveLength(1);
    expect(rows[0].textContent).toContain('Conversion out');
  });
});
