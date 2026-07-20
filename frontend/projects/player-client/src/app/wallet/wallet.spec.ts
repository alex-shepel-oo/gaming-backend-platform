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
    gameSelection.select({ id: 'game-1', slug: 'demo-shooter', name: 'Demo Shooter' });
  });

  afterEach(() => {
    httpMock.verify();
  });

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
  });

  it('requests the next page with an incremented page number', () => {
    const fixture = TestBed.createComponent(Wallet);

    httpMock.expectOne((req) => req.url === EconomyEndpoints.balances).flush([]);
    httpMock
      .expectOne((req) => req.url === EconomyEndpoints.transactions && req.params.get('page') === '1')
      .flush({ items: [], page: 1, pageSize: 20, totalCount: 40 });
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement).querySelector('button')!.dispatchEvent(new Event('click'));

    httpMock
      .expectOne((req) => req.url === EconomyEndpoints.transactions && req.params.get('page') === '2')
      .flush({ items: [], page: 2, pageSize: 20, totalCount: 40 });
  });
});
