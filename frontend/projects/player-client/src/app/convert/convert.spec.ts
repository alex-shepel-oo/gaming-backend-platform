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

const currencyCatalog = [
  { id: 'currency-a', code: 'GOLD', displayName: 'Gold', scope: CurrencyScope.Platform, gameId: null, decimals: 2 },
  { id: 'currency-b', code: 'GEMS', displayName: 'Gems', scope: CurrencyScope.Game, gameId: 'game-1', decimals: 2 },
];

const myGames = [{ id: 'game-1', slug: 'demo-shooter', name: 'Demo Shooter', description: null, iconUrl: null }];
const publicGames = myGames;

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

  function flushCurrencyCatalog(catalog: unknown[] = currencyCatalog): void {
    httpMock.expectOne((req) => req.url === EconomyEndpoints.currencies).flush(catalog);
  }

  function flushGamesLists(myGamesResponse: unknown[] = myGames, publicGamesResponse: unknown[] = publicGames): void {
    httpMock.expectOne((req) => req.url === IdentityGameEndpoints.myGames).flush(myGamesResponse);
    httpMock.expectOne((req) => req.url === IdentityGameEndpoints.publicGames).flush(publicGamesResponse);
  }

  function createWithCurrencies(
    balancesResponse: unknown[] = balances,
    catalog: unknown[] = currencyCatalog,
    myGamesResponse: unknown[] = myGames,
    publicGamesResponse: unknown[] = publicGames,
  ): ComponentFixture<Convert> {
    const currentFixture = TestBed.createComponent(Convert);
    httpMock.expectOne((req) => req.url === EconomyEndpoints.balances).flush(balancesResponse);
    flushCurrencyCatalog(catalog);
    flushGamesLists(myGamesResponse, publicGamesResponse);
    currentFixture.detectChanges();

    return currentFixture;
  }

  // Selecting a valid from/to/amount triple now also fires a GET for the
  // rate preview. Drain it here so tests that don't care about the preview
  // aren't left with an unflushed request tripping httpMock.verify().
  function drainPendingRateRequests(): void {
    // switchMap cancels a still-in-flight rate request the moment a newer
    // from/to/amount combination comes in. match() still returns those
    // cancelled requests, but flushing one throws, so only the still-live
    // ones need a response.
    httpMock
      .match((req) => req.url === EconomyEndpoints.conversionRate)
      .filter((req) => !req.cancelled)
      .forEach((req) => req.flush({ fromCurrencyId: 'currency-a', toCurrencyId: 'currency-b', rate: 100 }));
  }

  // The dropdown's own option list only renders in the DOM while it's open
  // (unlike a native <select>'s always-present <option>s), so this opens,
  // clicks the option carrying that id (see select-dropdown.html's
  // data-option-id), and leaves the popup closed again afterward.
  function selectDropdownOption(
    currentFixture: ComponentFixture<Convert>,
    formControlName: string,
    optionId: string,
  ): void {
    const element = currentFixture.nativeElement as HTMLElement;
    const dropdown = element.querySelector(`app-select-dropdown[formcontrolname="${formControlName}"]`) as HTMLElement;

    dropdown.querySelector<HTMLButtonElement>('.select-dropdown__trigger')!.click();
    currentFixture.detectChanges();

    dropdown.querySelector<HTMLLIElement>(`.select-dropdown__option[data-option-id="${optionId}"]`)!.click();
    currentFixture.detectChanges();
  }

  function fillAndSubmit(currentFixture: ComponentFixture<Convert>, amount = '10'): void {
    const element = currentFixture.nativeElement as HTMLElement;

    selectDropdownOption(currentFixture, 'fromCurrencyId', 'currency-a');
    // toCurrencyId auto-selects once fromCurrencyId narrows the catalog to a
    // single reachable option (the default balances/catalog fixtures always
    // land there), no click needed, same as the component's own auto-select.

    const amountInput = element.querySelector('input[type="number"]') as HTMLInputElement;
    amountInput.value = amount;
    amountInput.dispatchEvent(new Event('input'));
    currentFixture.detectChanges();

    drainPendingRateRequests();

    element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    currentFixture.detectChanges();
  }

  it('shows an empty state when the player has no currencies to convert', () => {
    fixture = TestBed.createComponent(Convert);
    httpMock.expectOne((req) => req.url === EconomyEndpoints.balances).flush([]);
    flushCurrencyCatalog();
    flushGamesLists();
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

  it('clamps a typed amount down to the available balance instead of allowing it to exceed', () => {
    fixture = createWithCurrencies();

    fillAndSubmitPrep(fixture, '1000');
    drainPendingRateRequests();

    const amountInput = (fixture.nativeElement as HTMLElement).querySelector(
      'input[type="number"]',
    ) as HTMLInputElement;
    expect(amountInput.value).toBe('100');

    const submitButton = (fixture.nativeElement as HTMLElement).querySelector(
      'button[type="submit"]',
    ) as HTMLButtonElement;
    expect(submitButton.disabled).toBe(false);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('Amount exceeds your available balance.');
  });

  it('auto-fills the amount to 1 as soon as a from-currency with a balance is selected', () => {
    fixture = createWithCurrencies();

    selectFrom(fixture, 'currency-a');
    drainPendingRateRequests();

    const amountInput = (fixture.nativeElement as HTMLElement).querySelector(
      'input[type="number"]',
    ) as HTMLInputElement;
    expect(amountInput.value).toBe('1');
  });

  it('does not show a game picker when only one game is available', () => {
    fixture = createWithCurrencies();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('Convert into game');

    drainPendingRateRequests();
  });

  it('includes a game currency the player has zero balance in among the To currency options', () => {
    // The reported bug: the player holds only platform currency and has never
    // transacted in Demo Shooter's currency, yet it must still be offered as
    // a conversion target because it now comes from the full catalog, not
    // from held balances.
    const onlyPlatformBalance = [balances[0]];

    fixture = createWithCurrencies(onlyPlatformBalance, currencyCatalog, myGames, publicGames);

    selectDropdownOption(fixture, 'fromCurrencyId', 'currency-a');

    // toCurrencyDropdownOptions is exactly what the "To" dropdown renders --
    // checking it directly avoids needing the popup open, which a single-
    // option (auto-selected, readonly) dropdown never does.
    // eslint-disable-next-line @typescript-eslint/dot-notation
    const toOptionIds = fixture.componentInstance['toCurrencyDropdownOptions']().map(
      (option: { id: string }) => option.id,
    );

    expect(toOptionIds).toContain('currency-b');

    drainPendingRateRequests();
  });

  it("orders the game picker with the player's own games first, then other public games", () => {
    const secondGame = { id: 'game-2', slug: 'demo-puzzle', name: 'Demo Puzzle', description: null, iconUrl: null };
    const catalogWithTwoGames = [
      ...currencyCatalog,
      { id: 'currency-c', code: 'PUZZLE_GEMS', displayName: 'Puzzle Gems', scope: CurrencyScope.Game, gameId: 'game-2', decimals: 2 },
    ];

    fixture = createWithCurrencies(balances, catalogWithTwoGames, myGames, [...publicGames, secondGame]);

    selectDropdownOption(fixture, 'fromCurrencyId', 'currency-a');

    // eslint-disable-next-line @typescript-eslint/dot-notation
    const gameOptionLabels = fixture.componentInstance['targetGameDropdownOptions']().map(
      (option: { label: string }) => option.label,
    );

    expect(gameOptionLabels).toEqual(['Demo Shooter', 'Demo Puzzle']);

    drainPendingRateRequests();
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

    // The shell toolbar reads this same shared signal, proving it also
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

  const secondGame = { id: 'game-2', slug: 'demo-puzzle', name: 'Demo Puzzle', description: null, iconUrl: null };
  const catalogWithTwoGames = [
    ...currencyCatalog,
    { id: 'currency-c', code: 'PUZZLE_GEMS', displayName: 'Puzzle Gems', scope: CurrencyScope.Game, gameId: 'game-2', decimals: 2 },
  ];
  const balancesWithTwoGames = [
    ...balances,
    { currencyId: 'currency-c', currencyCode: 'PUZZLE_GEMS', scope: CurrencyScope.Game, gameId: 'game-2', amount: 5 },
  ];

  function selectFrom(currentFixture: ComponentFixture<Convert>, currencyId: string): void {
    selectDropdownOption(currentFixture, 'fromCurrencyId', currencyId);
  }

  function selectTargetGame(currentFixture: ComponentFixture<Convert>, gameId: string): void {
    selectDropdownOption(currentFixture, 'targetGameId', gameId);
  }

  it('narrows to-currency to exactly the platform currency, with no game picker, when from is a game currency', () => {
    fixture = createWithCurrencies(balancesWithTwoGames, catalogWithTwoGames, myGames, [...publicGames, secondGame]);

    selectFrom(fixture, 'currency-b');

    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent ?? '').not.toContain('Convert into game');

    // eslint-disable-next-line @typescript-eslint/dot-notation
    const toOptionIds = fixture.componentInstance['toCurrencyDropdownOptions']().map(
      (option: { id: string }) => option.id,
    );
    expect(toOptionIds).toEqual(['currency-a']);

    // Auto-selected, per 3c: a single-candidate set is not a real choice.
    // eslint-disable-next-line @typescript-eslint/dot-notation
    expect(fixture.componentInstance['form'].value.toCurrencyId).toBe('currency-a');

    drainPendingRateRequests();
  });

  it('auto-selects the only currency of a game with exactly one currency, once that game is picked', () => {
    fixture = createWithCurrencies(balancesWithTwoGames, catalogWithTwoGames, myGames, [...publicGames, secondGame]);

    selectFrom(fixture, 'currency-a');
    selectTargetGame(fixture, 'game-2');

    // eslint-disable-next-line @typescript-eslint/dot-notation
    expect(fixture.componentInstance['form'].value.toCurrencyId).toBe('currency-c');

    drainPendingRateRequests();
  });

  it('clears the stale target-game and to-currency selections when from changes to a different currency', () => {
    fixture = createWithCurrencies(balancesWithTwoGames, catalogWithTwoGames, myGames, [...publicGames, secondGame]);

    selectFrom(fixture, 'currency-a');
    selectTargetGame(fixture, 'game-2');

    // eslint-disable-next-line @typescript-eslint/dot-notation
    expect(fixture.componentInstance['form'].value.toCurrencyId).toBe('currency-c');

    // Switching from to a game currency invalidates both the previous game
    // pick and the previous to-currency pick, neither is a live option for
    // this new from anymore, so both must be back to empty (then re-populated
    // by auto-select, which for this from narrows to platform).
    selectFrom(fixture, 'currency-b');

    // eslint-disable-next-line @typescript-eslint/dot-notation
    expect(fixture.componentInstance['form'].value.targetGameId).toBe('');
    // eslint-disable-next-line @typescript-eslint/dot-notation
    expect(fixture.componentInstance['form'].value.toCurrencyId).toBe('currency-a');

    drainPendingRateRequests();
  });

  it('re-enables submit after the game picker is hidden by a from-is-game-currency switch, once an amount is entered', () => {
    // Regression case: the game picker's native `required` attribute gets
    // picked up by Angular's RequiredValidator directive, which composes
    // Validators.required onto targetGameId's control, and that stays
    // attached even after the picker's element is removed from the DOM,
    // since Angular doesn't clear directive-contributed validators on
    // destroy. Resetting targetGameId back to '' in that state used to
    // permanently fail that orphaned validator, leaving the form invalid
    // forever even though targetGameId is never required by the component
    // (it has no Validators.required of its own) and is no longer shown.
    fixture = createWithCurrencies(balancesWithTwoGames, catalogWithTwoGames, myGames, [...publicGames, secondGame]);

    selectFrom(fixture, 'currency-a');
    selectTargetGame(fixture, 'game-2');
    selectFrom(fixture, 'currency-b');

    const element = fixture.nativeElement as HTMLElement;
    const amountInput = element.querySelector('input[type="number"]') as HTMLInputElement;
    amountInput.value = '10';
    amountInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    drainPendingRateRequests();

    const submitButton = element.querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(submitButton.disabled).toBe(false);
  });

  it('disables submit when toCurrencyId no longer matches a live option even though the control still has a value', () => {
    fixture = createWithCurrencies();
    fillAndSubmitPrep(fixture);
    drainPendingRateRequests();

    const submitButton = (fixture.nativeElement as HTMLElement).querySelector(
      'button[type="submit"]',
    ) as HTMLButtonElement;
    expect(submitButton.disabled).toBe(false);

    // eslint-disable-next-line @typescript-eslint/dot-notation
    const toCurrencyControl = fixture.componentInstance['form'].controls.toCurrencyId;
    toCurrencyControl.setValue('no-longer-offered', { emitEvent: false });
    toCurrencyControl.updateValueAndValidity({ emitEvent: false });
    fixture.detectChanges();

    expect(submitButton.disabled).toBe(true);
  });

  function fillAndSubmitPrep(currentFixture: ComponentFixture<Convert>, amount = '10'): void {
    selectFrom(currentFixture, 'currency-a');
    // toCurrencyId auto-selects to 'currency-b' once fromCurrencyId narrows
    // the catalog to that single reachable option, no click needed.

    const element = currentFixture.nativeElement as HTMLElement;
    const amountInput = element.querySelector('input[type="number"]') as HTMLInputElement;
    amountInput.value = amount;
    amountInput.dispatchEvent(new Event('input'));
    currentFixture.detectChanges();
  }

  describe('conversion rate preview', () => {
    // fillAndSubmitPrep's own from/to/amount steps each supersede the last
    // via switchMap (a from-currency pick alone can already auto-complete
    // to-currency and the auto-filled amount, all before the amount this
    // test actually cares about is typed). expectOne() counts cancelled
    // requests too, so this needs the same live-only filter
    // drainPendingRateRequests() uses instead of matching blind.
    function flushRate(rate: number): void {
      const live = httpMock
        .match((req) => req.url === EconomyEndpoints.conversionRate)
        .filter((req) => !req.cancelled);

      expect(live).toHaveLength(1);
      live[0].flush({ fromCurrencyId: 'currency-a', toCurrencyId: 'currency-b', rate });
    }

    it('auto-selects a default currency pair on load, without the player having to choose', () => {
      // With the placeholder "Select a currency"/"Select a game" options
      // gone, a bare <select> would otherwise just visually default to its
      // first option while the form control silently stayed empty --
      // picking a real default for real keeps the two in sync from the start.
      fixture = createWithCurrencies();

      const element = fixture.nativeElement as HTMLElement;
      // eslint-disable-next-line @typescript-eslint/dot-notation
      const formValue = fixture.componentInstance['form'].value;

      expect(formValue.fromCurrencyId).toBe('currency-a');
      expect(formValue.toCurrencyId).toBe('currency-b');

      const placeholder = element.querySelector('.convert-to-card__placeholder');
      expect(placeholder?.classList.contains('convert-to-card__placeholder--hidden')).toBe(true);

      drainPendingRateRequests();
    });

    it('fetches and shows the computed preview once from, to, and amount are all set', () => {
      fixture = createWithCurrencies();

      fillAndSubmitPrep(fixture, '10');
      flushRate(100);
      fixture.detectChanges();

      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('1000 GEMS');
    });

    it('recomputes the preview when fromAmount changes', () => {
      fixture = createWithCurrencies();

      fillAndSubmitPrep(fixture, '10');
      flushRate(100);
      fixture.detectChanges();

      const amountInput = (fixture.nativeElement as HTMLElement).querySelector(
        'input[type="number"]',
      ) as HTMLInputElement;
      amountInput.value = '5';
      amountInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      flushRate(100);
      fixture.detectChanges();

      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('500 GEMS');
    });

    it('falls back to hiding the preview, without crashing, when the rate request fails', () => {
      fixture = createWithCurrencies();

      fillAndSubmitPrep(fixture, '10');
      httpMock
        .match((req) => req.url === EconomyEndpoints.conversionRate)
        .filter((req) => !req.cancelled)
        .forEach((req) =>
          req.flush({ status: 400, title: 'Unsupported conversion pair' }, { status: 400, statusText: 'Bad Request' }),
        );
      fixture.detectChanges();

      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('Preview unavailable');
    });
  });
});
