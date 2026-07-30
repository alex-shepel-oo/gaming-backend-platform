import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { EconomyEndpoints } from './economy-endpoints';
import { CurrencyScope } from './wallet.models';
import { WalletService } from './wallet.service';

describe('WalletService balances snapshot', () => {
  let httpMock: HttpTestingController;
  let service: WalletService;

  const balances = [
    { currencyId: 'platform-1', currencyCode: 'PLATFORM_CREDITS', scope: CurrencyScope.Platform, gameId: null, amount: 500 },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    httpMock = TestBed.inject(HttpTestingController);
    service = TestBed.inject(WalletService);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('starts with no cached balances', () => {
    expect(service.balances()).toBeNull();
  });

  it('populates the shared signal after refreshBalances resolves', () => {
    service.refreshBalances().subscribe();

    httpMock.expectOne((req) => req.url === EconomyEndpoints.balances).flush(balances);

    expect(service.balances()).toEqual(balances);
  });

  it('clearBalances resets the signal back to null', () => {
    service.refreshBalances().subscribe();
    httpMock.expectOne((req) => req.url === EconomyEndpoints.balances).flush(balances);

    service.clearBalances();

    expect(service.balances()).toBeNull();
  });

  it('applyBalanceChange updates the matching currency and leaves others untouched', () => {
    const twoBalances = [
      ...balances,
      { currencyId: 'game-1', currencyCode: 'GEMS', scope: CurrencyScope.Game, gameId: 'game-a', amount: 10 },
    ];

    service.refreshBalances().subscribe();
    httpMock.expectOne((req) => req.url === EconomyEndpoints.balances).flush(twoBalances);

    service.applyBalanceChange('platform-1', 750);

    expect(service.balances()).toEqual([
      { ...twoBalances[0], amount: 750 },
      twoBalances[1],
    ]);
  });

  it('getBalances requests the balances endpoint with no game-scoping params', () => {
    service.getBalances().subscribe();

    const request = httpMock.expectOne((req) => req.url === EconomyEndpoints.balances);
    expect(request.request.params.keys().length).toBe(0);
    request.flush(balances);
  });

  it('getCurrencies fetches the full currency catalog', () => {
    const currencies = [
      { id: 'platform-1', code: 'PLATFORM_CREDITS', displayName: 'Platform Credits', scope: CurrencyScope.Platform, gameId: null, decimals: 2 },
      { id: 'game-1', code: 'GEMS', displayName: 'Gems', scope: CurrencyScope.Game, gameId: 'game-a', decimals: 2 },
    ];

    let result: unknown;
    service.getCurrencies().subscribe((response) => (result = response));

    httpMock.expectOne((req) => req.url === EconomyEndpoints.currencies).flush(currencies);

    expect(result).toEqual(currencies);
  });
});
