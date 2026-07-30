import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { combineLatest, of, Subscription, timer } from 'rxjs';
import { catchError, map, startWith, switchMap, takeWhile, tap } from 'rxjs/operators';
import {
  Balance,
  Conversion,
  ConversionService,
  ConversionStatus,
  Currency,
  GamesService,
  PublicGame,
  WalletService,
  isTerminalConversionStatus,
} from 'shared';
import { NotAvailable } from '../ui/not-available/not-available';

interface ConvertPreview {
  amount: string;
  code: string;
}

const POLL_INTERVAL_MS = 1500;

type ConvertError = 'insufficient-funds' | 'conflict';

function classifyConvertError(error: unknown): ConvertError {
  if (error instanceof HttpErrorResponse && error.status === 402) {
    return 'insufficient-funds';
  }

  return 'conflict';
}

const STATUS_LABELS: Record<ConversionStatus, string> = {
  [ConversionStatus.Started]: 'Started',
  [ConversionStatus.DebitDone]: 'Processing',
  [ConversionStatus.Completed]: 'Completed',
  [ConversionStatus.Compensating]: 'Reversing',
  [ConversionStatus.Failed]: 'Failed',
};

const STATUS_STYLE_CLASSES: Record<ConversionStatus, string> = {
  [ConversionStatus.Started]: 'status-progress',
  [ConversionStatus.DebitDone]: 'status-progress',
  [ConversionStatus.Completed]: 'status-success',
  [ConversionStatus.Compensating]: 'status-warning',
  [ConversionStatus.Failed]: 'status-error',
};

@Component({
  selector: 'app-convert',
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    NotAvailable,
  ],
  templateUrl: './convert.html',
  styleUrl: './convert.scss',
})
export class Convert {
  private readonly formBuilder = inject(FormBuilder);
  private readonly conversionService = inject(ConversionService);
  private readonly walletService = inject(WalletService);
  private readonly gamesService = inject(GamesService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly ConversionStatus = ConversionStatus;

  // Currencies the player actually holds -- you can only convert money you
  // have, so "from" stays scoped to balances.
  protected readonly currencies = signal<Balance[]>([]);
  protected readonly currenciesLoading = signal(true);
  protected readonly currenciesError = signal(false);

  // The full currency catalog, independent of held balances -- a currency the
  // player has never transacted in (zero balance) must still be reachable as
  // a conversion target, which is exactly what "from" balances alone cannot
  // offer.
  protected readonly currencyCatalog = signal<Currency[]>([]);

  // Games ordered with the player's own games (listMyGames) first, followed
  // by any other public game (listPublicGames) not already in that set.
  protected readonly orderedGames = signal<PublicGame[]>([]);

  private readonly insufficientBalanceValidator: ValidatorFn = (control) => {
    const fromCurrencyId = control.parent?.get('fromCurrencyId')?.value as string | undefined;
    const currency = this.currencies().find((candidate) => candidate.currencyId === fromCurrencyId);

    return currency && control.value > currency.amount ? { insufficientBalance: true } : null;
  };

  // A toCurrencyId control can hold a value that was valid a moment ago but
  // no longer is -- e.g. the user picked a game currency as "to", then
  // changed "from" to a game currency itself, which per the conversion
  // topology narrows "to" down to the platform currency only. Validators.required
  // alone can't see that: the control still has a non-empty value, it's just
  // not a live option anymore. Re-checked via applyToCurrencyAutoSelect(),
  // called everywhere the candidate set can change (see its own comment).
  private readonly toCurrencyStillOfferedValidator: ValidatorFn = (control) => {
    const value = control.value as string;

    if (!value) {
      return null;
    }

    return this.toCurrencyOptions().some((currency) => currency.id === value) ? null : { notOffered: true };
  };

  // Same staleness concern as above, but for "from": it's built from held
  // balances, which can change out from under a previous selection (e.g.
  // after a conversion drains a balance to zero).
  private readonly fromCurrencyStillHeldValidator: ValidatorFn = (control) => {
    const value = control.value as string;

    if (!value) {
      return null;
    }

    return this.currencies().some((currency) => currency.currencyId === value) ? null : { notHeld: true };
  };

  protected readonly form = this.formBuilder.nonNullable.group({
    fromCurrencyId: ['', [Validators.required, this.fromCurrencyStillHeldValidator]],
    targetGameId: [''],
    toCurrencyId: ['', [Validators.required, this.toCurrencyStillOfferedValidator]],
    fromAmount: [0, [Validators.required, Validators.min(0.01), this.insufficientBalanceValidator]],
  });

  protected readonly submitting = signal(false);
  protected readonly error = signal<ConvertError | null>(null);
  protected readonly conversion = signal<Conversion | null>(null);

  // computed() only tracks signal reads -- reading a FormControl's plain
  // .value here would never invalidate the cache when the user picks a new
  // value, so the currency/game pickers below must react off these signals
  // (kept in sync via valueChanges in the constructor), not the form directly.
  private readonly fromCurrencyIdValue = signal('');
  private readonly targetGameIdValue = signal('');

  private readonly rawToCurrencyOptions = computed(() => {
    const fromCurrencyId = this.fromCurrencyIdValue();

    return this.currencyCatalog().filter((currency) => currency.id !== fromCurrencyId);
  });

  private readonly fromCurrency = computed(() =>
    this.currencyCatalog().find((currency) => currency.id === this.fromCurrencyIdValue()) ?? null,
  );

  // A game currency may only ever convert back to the platform currency,
  // never to another game's currency -- there is no rate for that pairing
  // and there never will be per the settled conversion topology.
  protected readonly fromIsGameCurrency = computed(() => this.fromCurrency()?.gameId != null);

  private readonly platformCurrency = computed(() =>
    this.currencyCatalog().find((currency) => currency.gameId === null) ?? null,
  );

  // Only games that actually have a reachable currency (i.e. survive the
  // fromCurrencyId exclusion above) are offered, ordered per orderedGames.
  // None are offered at all once "from" is itself a game currency -- there's
  // no game left to pick, the destination is fixed to the platform currency.
  protected readonly toGameOptions = computed(() => {
    if (this.fromIsGameCurrency()) {
      return [];
    }

    const gameIdsWithCurrency = new Set(
      this.rawToCurrencyOptions()
        .filter((currency) => currency.gameId !== null)
        .map((currency) => currency.gameId as string),
    );

    return this.orderedGames().filter((game) => gameIdsWithCurrency.has(game.id));
  });

  protected readonly showGamePicker = computed(() => !this.fromIsGameCurrency() && this.toGameOptions().length > 1);

  protected readonly toCurrencyOptions = computed(() => {
    if (this.fromIsGameCurrency()) {
      const platform = this.platformCurrency();
      return platform ? [platform] : [];
    }

    const targetGameId = this.targetGameIdValue();

    return this.rawToCurrencyOptions().filter((currency) => {
      // Platform stays reachable regardless of which game is picked --
      // widening reachable game currencies must not narrow this out.
      if (currency.gameId === null) {
        return true;
      }

      return !this.showGamePicker() || currency.gameId === targetGameId;
    });
  });

  protected readonly preview = signal<ConvertPreview | null>(null);

  private pollSubscription: Subscription | null = null;

  constructor() {
    this.destroyRef.onDestroy(() => this.pollSubscription?.unsubscribe());

    this.walletService.getBalances().subscribe({
      next: (balances) => {
        this.currencies.set(balances);
        this.currenciesLoading.set(false);

        // emitEvent: false on both -- these only need their OWN validators
        // (insufficientBalance, fromCurrencyStillHeld) re-checked against the
        // freshly loaded balances. Without it, updateValueAndValidity's
        // default emitEvent:true would re-fire fromCurrencyId's own
        // valueChanges cascade below and wipe out a selection the user
        // hasn't actually touched.
        this.form.controls.fromAmount.updateValueAndValidity({ emitEvent: false });
        this.form.controls.fromCurrencyId.updateValueAndValidity({ emitEvent: false });
      },
      error: () => {
        this.currenciesLoading.set(false);
        this.currenciesError.set(true);
      },
    });

    this.walletService.getCurrencies().subscribe((currencies) => {
      this.currencyCatalog.set(currencies);
      this.applyToCurrencyAutoSelect();
    });

    combineLatest([this.gamesService.listMyGames(), this.gamesService.listPublicGames()]).subscribe(
      ([myGames, publicGames]) => {
        const myGameIds = new Set(myGames.map((game) => game.id));
        const otherGames = publicGames.filter((game) => !myGameIds.has(game.id));

        this.orderedGames.set([...myGames, ...otherGames]);
        this.applyToCurrencyAutoSelect();
      },
    );

    this.form.controls.fromCurrencyId.valueChanges.subscribe((value) => {
      this.fromCurrencyIdValue.set(value);
      this.form.controls.fromAmount.updateValueAndValidity({ emitEvent: false });

      // The set of valid "to" options just changed underneath whatever was
      // previously picked -- resetting targetGameId (always, even if it was
      // already empty -- setValue still emits) cascades into the
      // targetGameId handler below, which resets toCurrencyId in turn and
      // re-runs auto-select against the now-current fromCurrencyId. Doing
      // that reset here too, on top of the cascade, would just re-clobber
      // whatever the cascade already auto-selected.
      this.form.controls.targetGameId.setValue('');
    });

    this.form.controls.targetGameId.valueChanges.subscribe((value) => {
      this.targetGameIdValue.set(value);
      this.form.controls.toCurrencyId.setValue('');
      this.applyToCurrencyAutoSelect();
    });

    this.buildPreviewPipeline();
  }

  // Whenever the live "to" candidate set narrows to exactly one entry, that's
  // no longer a real choice for the user to make -- select it for them.
  // Otherwise, re-runs toCurrencyStillOfferedValidator against the set as it
  // stands now, since a control can hold a value that was valid a moment ago
  // (e.g. the catalog finished loading after a currency was already picked).
  private applyToCurrencyAutoSelect(): void {
    const options = this.toCurrencyOptions();
    const toCurrencyControl = this.form.controls.toCurrencyId;

    if (options.length === 1 && toCurrencyControl.value !== options[0].id) {
      toCurrencyControl.setValue(options[0].id);
    } else {
      toCurrencyControl.updateValueAndValidity({ emitEvent: false });
    }
  }

  private buildPreviewPipeline(): void {
    const fromCurrencyId$ = this.form.controls.fromCurrencyId.valueChanges.pipe(
      startWith(this.form.controls.fromCurrencyId.value),
    );
    const toCurrencyId$ = this.form.controls.toCurrencyId.valueChanges.pipe(
      startWith(this.form.controls.toCurrencyId.value),
    );
    const fromAmount$ = this.form.controls.fromAmount.valueChanges.pipe(
      startWith(this.form.controls.fromAmount.value),
    );

    combineLatest([fromCurrencyId$, toCurrencyId$, fromAmount$])
      .pipe(
        switchMap(([fromCurrencyId, toCurrencyId, fromAmount]) => {
          if (!fromCurrencyId || !toCurrencyId || !fromAmount || fromAmount <= 0) {
            return of(null);
          }

          return this.conversionService.rate(fromCurrencyId, toCurrencyId).pipe(
            map((rateDto): ConvertPreview => {
              const toCurrency = this.currencyCatalog().find((currency) => currency.id === toCurrencyId);
              const decimals = toCurrency?.decimals ?? 2;

              return { amount: (fromAmount * rateDto.rate).toFixed(decimals), code: toCurrency?.code ?? '' };
            }),
            // A stale/edge-case pair (or a request that outruns 3b's own
            // constraints) must not turn into an unhandled console error --
            // just fall back to hiding the preview, same as "not selected yet".
            catchError(() => of(null)),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((preview) => this.preview.set(preview));
  }

  protected statusLabel(status: ConversionStatus): string {
    return STATUS_LABELS[status];
  }

  protected statusStyleClass(status: ConversionStatus): string {
    return STATUS_STYLE_CLASSES[status];
  }

  protected submit(): void {
    if (this.form.invalid) {
      return;
    }

    this.pollSubscription?.unsubscribe();
    this.submitting.set(true);
    this.error.set(null);
    this.conversion.set(null);

    // Generated once per user click ("new intent"), not per HTTP attempt --
    // if this same request were ever retried at the network layer, it would
    // reuse this same key rather than call randomUUID() again, since the
    // key is captured here and reused by the single POST built from it.
    const idempotencyKey = crypto.randomUUID();
    const { fromCurrencyId, toCurrencyId, fromAmount } = this.form.getRawValue();

    this.conversionService.create({ fromCurrencyId, toCurrencyId, fromAmount }, idempotencyKey).subscribe({
      next: (conversion) => {
        this.submitting.set(false);
        this.conversion.set(conversion);
        this.startPolling(conversion.conversionId);
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.error.set(classifyConvertError(error));
      },
    });
  }

  private startPolling(conversionId: string): void {
    this.pollSubscription = timer(POLL_INTERVAL_MS, POLL_INTERVAL_MS)
      .pipe(
        switchMap(() => this.conversionService.get(conversionId)),
        tap((conversion) => {
          this.conversion.set(conversion);

          if (conversion.status === ConversionStatus.Completed) {
            this.walletService.refreshBalances().subscribe((balances) => {
              this.currencies.set(balances);
              this.form.controls.fromCurrencyId.updateValueAndValidity({ emitEvent: false });
            });
          }
        }),
        takeWhile((conversion) => !isTerminalConversionStatus(conversion.status), true),
      )
      .subscribe();
  }
}
