import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Conversion, ConversionStatus, CurrencyScope, EconomyEndpoints, IdentityGameEndpoints, WalletService } from 'shared';
import { Convert } from './convert';

const POLL_INTERVAL_MS = 1500;

const balances = [
  { currencyId: 'currency-a', currencyCode: 'GOLD', scope: CurrencyScope.Platform, gameId: null, amount: 100 },
  { currencyId: 'currency-b', currencyCode: 'GEMS', scope: CurrencyScope.Game, gameId: 'game-1', amount: 50 },
];

function conversionResponse(conversionId: string, status: ConversionStatus, overrides: Partial<Conversion> = {}): Conversion {
  return {
    conversionId,
    userId: 'user-1',
    fromCurrencyId: 'currency-a',
    toCurrencyId: 'currency-b',
    fromAmount: 10,
    toAmount: 1000,
    rateApplied: 100,
    status,
    failureReason: null,
    createdAt: '2026-07-20T00:00:00Z',
    updatedAt: '2026-07-20T00:00:00Z',
    ...overrides,
  };
}

describe('Convert', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<Convert> | undefined;

  beforeEach(() => {
    vi.useFakeTimers();

    TestBed.configureTestingModule({
      imports: [Convert],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    fixture?.destroy();
    httpMock.verify();
    vi.useRealTimers();
  });

  function flushGamesList(): void {
    httpMock
      .expectOne((req) => req.url === IdentityGameEndpoints.publicGames)
      .flush([{ id: 'game-1', slug: 'demo-shooter', name: 'Demo Shooter' }]);
  }

  function createWithCurrencies(balancesResponse: unknown[] = balances): ComponentFixture<Convert> {
    const currentFixture = TestBed.createComponent(Convert);
    httpMock.expectOne((req) => req.url === EconomyEndpoints.balances).flush(balancesResponse);
    flushGamesList();
    currentFixture.detectChanges();

    return currentFixture;
  }

  function fillAndSubmit(currentFixture: ComponentFixture<Convert>, amount = '10'): void {
    const element = currentFixture.nativeElement as HTMLElement;
    const fromSelect = element.querySelector('select[formcontrolname="fromCurrencyId"]') as HTMLSelectElement;
    const toSelect = element.querySelector('select[formcontrolname="toCurrencyId"]') as HTMLSelectElement;

    fromSelect.value = 'currency-a';
    fromSelect.dispatchEvent(new Event('change'));
    currentFixture.detectChanges();

    toSelect.value = 'currency-b';
    toSelect.dispatchEvent(new Event('change'));

    const amountInput = element.querySelector('input[type="number"]') as HTMLInputElement;
    amountInput.value = amount;
    amountInput.dispatchEvent(new Event('input'));
    currentFixture.detectChanges();

    element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    currentFixture.detectChanges();
  }

  it('shows an empty state when the player has no currencies to convert', () => {
    fixture = TestBed.createComponent(Convert);
    httpMock.expectOne((req) => req.url === EconomyEndpoints.balances).flush([]);
    flushGamesList();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain("don't have any currencies to convert yet");
  });

  it('generates a new Idempotency-Key for every new submit, not just the first', () => {
    fixture = createWithCurrencies();

    fillAndSubmit(fixture);
    const firstRequest = httpMock.expectOne(EconomyEndpoints.conversions);
    const firstKey = firstRequest.request.headers.get('Idempotency-Key');
    firstRequest.flush(conversionResponse('conv-1', ConversionStatus.Started), {
      status: 202,
      statusText: 'Accepted',
    });

    fillAndSubmit(fixture);
    const secondRequest = httpMock.expectOne(EconomyEndpoints.conversions);
    const secondKey = secondRequest.request.headers.get('Idempotency-Key');
    secondRequest.flush(conversionResponse('conv-2', ConversionStatus.Started), {
      status: 202,
      statusText: 'Accepted',
    });

    expect(firstKey).toBeTruthy();
    expect(secondKey).toBeTruthy();
    expect(secondKey).not.toBe(firstKey);
  });

  it('disables the submit button when the requested amount exceeds the available balance', () => {
    fixture = createWithCurrencies();

    fillAndSubmit(fixture, '1000');

    const submitButton = (fixture.nativeElement as HTMLElement).querySelector(
      'button[type="submit"]',
    ) as HTMLButtonElement;
    expect(submitButton.disabled).toBe(true);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Amount exceeds your available balance.');
  });

  it('does not show a game picker when only one game is available', () => {
    fixture = createWithCurrencies();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('Convert into game');
  });

  it('polls the conversion status until it reaches Completed, refreshing balances, then stops', () => {
    fixture = createWithCurrencies();

    fillAndSubmit(fixture);
    httpMock
      .expectOne(EconomyEndpoints.conversions)
      .flush(conversionResponse('conv-1', ConversionStatus.Started), { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();

    let text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Status: Started');

    vi.advanceTimersByTime(POLL_INTERVAL_MS);
    httpMock.expectOne(EconomyEndpoints.conversion('conv-1')).flush(conversionResponse('conv-1', ConversionStatus.DebitDone));
    fixture.detectChanges();

    text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Status: Processing');

    vi.advanceTimersByTime(POLL_INTERVAL_MS);
    httpMock.expectOne(EconomyEndpoints.conversion('conv-1')).flush(conversionResponse('conv-1', ConversionStatus.Completed));
    fixture.detectChanges();

    httpMock
      .expectOne((req) => req.url === EconomyEndpoints.balances)
      .flush([{ ...balances[0], amount: 90 }, balances[1]]);
    fixture.detectChanges();

    text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Status: Completed');
    expect(text).toContain('90 available');

    // The shell toolbar reads this same shared signal -- proving it also
    // reflects the post-conversion balance, not just Convert's own view.
    expect(TestBed.inject(WalletService).balances()).toEqual([{ ...balances[0], amount: 90 }, balances[1]]);

    vi.advanceTimersByTime(POLL_INTERVAL_MS * 3);
    expect(httpMock.match(EconomyEndpoints.conversion('conv-1'))).toHaveLength(0);
  });

  it('polls the conversion status until it reaches Failed, then stops', () => {
    fixture = createWithCurrencies();

    fillAndSubmit(fixture);
    httpMock
      .expectOne(EconomyEndpoints.conversions)
      .flush(conversionResponse('conv-2', ConversionStatus.Started), { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();

    vi.advanceTimersByTime(POLL_INTERVAL_MS);
    httpMock
      .expectOne(EconomyEndpoints.conversion('conv-2'))
      .flush(conversionResponse('conv-2', ConversionStatus.Compensating));
    fixture.detectChanges();

    vi.advanceTimersByTime(POLL_INTERVAL_MS);
    httpMock.expectOne(EconomyEndpoints.conversion('conv-2')).flush(
      conversionResponse('conv-2', ConversionStatus.Failed, { failureReason: 'unsupported conversion pair' }),
    );
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Status: Failed');
    expect(text).toContain('unsupported conversion pair');

    vi.advanceTimersByTime(POLL_INTERVAL_MS * 3);
    expect(httpMock.match(EconomyEndpoints.conversion('conv-2'))).toHaveLength(0);
  });

  it('shows an insufficient-funds message on a 402, before any conversionId is known', () => {
    fixture = createWithCurrencies();

    fillAndSubmit(fixture);
    httpMock
      .expectOne(EconomyEndpoints.conversions)
      .flush({ status: 402, title: 'Insufficient funds' }, { status: 402, statusText: 'Payment Required' });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Insufficient funds for this conversion.');
    expect(text).not.toContain('Status:');
  });

  it('shows a generic error on a 409 idempotency-key conflict', () => {
    fixture = createWithCurrencies();

    fillAndSubmit(fixture);
    httpMock
      .expectOne(EconomyEndpoints.conversions)
      .flush({ status: 409, title: 'Idempotency-Key conflict' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('This conversion could not be started. Please try again.');
    expect(text).not.toContain('Status:');
  });
});
