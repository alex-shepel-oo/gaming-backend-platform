import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CurrencyScope, EconomyEndpoints, GameSelectionService, TransactionType } from 'shared';
import { Wallet } from './wallet';

describe('Wallet', () => {
  let httpMock: HttpTestingController;
  let gameSelection: GameSelectionService;

  const balances = [
    { currencyId: 'platform-1', currencyCode: 'PLATFORM_CREDITS', scope: CurrencyScope.Platform, gameId: null, amount: 500 },
    { currencyId: 'game-1', currencyCode: 'SHOOTER_GOLD', scope: CurrencyScope.Game, gameId: 'game-1', amount: 10 },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Wallet],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    httpMock = TestBed.inject(HttpTestingController);
    gameSelection = TestBed.inject(GameSelectionService);
    gameSelection.select({ id: 'game-1', slug: 'demo-shooter', name: 'Demo Shooter', description: null, iconUrl: null });
  });

  afterEach(() => {
    httpMock.verify();
  });

  function nextPageButton(fixture: { nativeElement: unknown }): HTMLButtonElement {
    const element = fixture.nativeElement as HTMLElement;

    return Array.from(element.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Next page'),
    ) as HTMLButtonElement;
  }

  it('renders platform and in-game balances and the transaction history', () => {
    const fixture = TestBed.createComponent(Wallet);

    httpMock.expectOne((req) => req.url === EconomyEndpoints.balances).flush(balances);
    httpMock.expectOne((req) => req.url === EconomyEndpoints.transactions).flush({
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
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('PLATFORM_CREDITS');
    expect(text).toContain('SHOOTER_GOLD');
    expect(text).toContain('platform-1');
    expect(text).toContain('Grant');
  });

  it('shows an empty state when there are no balances or transactions yet', () => {
    const fixture = TestBed.createComponent(Wallet);

    httpMock.expectOne((req) => req.url === EconomyEndpoints.balances).flush([]);
    httpMock
      .expectOne((req) => req.url === EconomyEndpoints.transactions)
      .flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No balances yet.');
    expect(text).toContain('No transactions yet.');
  });

  it('shows an error state when balances fail to load', () => {
    const fixture = TestBed.createComponent(Wallet);

    httpMock
      .expectOne((req) => req.url === EconomyEndpoints.balances)
      .flush({ status: 500, title: 'Server error' }, { status: 500, statusText: 'Internal Server Error' });
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
});
