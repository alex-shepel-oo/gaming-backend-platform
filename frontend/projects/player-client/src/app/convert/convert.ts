import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { combineLatest, Subscription, timer } from 'rxjs';
import { switchMap, takeWhile, tap } from 'rxjs/operators';
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

  protected readonly form = this.formBuilder.nonNullable.group({
    fromCurrencyId: ['', Validators.required],
    targetGameId: [''],
    toCurrencyId: ['', Validators.required],
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

  // Only games that actually have a reachable currency (i.e. survive the
  // fromCurrencyId exclusion above) are offered, ordered per orderedGames.
  protected readonly toGameOptions = computed(() => {
    const gameIdsWithCurrency = new Set(
      this.rawToCurrencyOptions()
        .filter((currency) => currency.gameId !== null)
        .map((currency) => currency.gameId as string),
    );

    return this.orderedGames().filter((game) => gameIdsWithCurrency.has(game.id));
  });

  protected readonly showGamePicker = computed(() => this.toGameOptions().length > 1);

  protected readonly toCurrencyOptions = computed(() => {
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

  private pollSubscription: Subscription | null = null;

  constructor() {
    this.destroyRef.onDestroy(() => this.pollSubscription?.unsubscribe());

    this.walletService.getBalances().subscribe({
      next: (balances) => {
        this.currencies.set(balances);
        this.currenciesLoading.set(false);
        this.form.controls.fromAmount.updateValueAndValidity();
      },
      error: () => {
        this.currenciesLoading.set(false);
        this.currenciesError.set(true);
      },
    });

    this.walletService.getCurrencies().subscribe((currencies) => {
      this.currencyCatalog.set(currencies);
    });

    combineLatest([this.gamesService.listMyGames(), this.gamesService.listPublicGames()]).subscribe(
      ([myGames, publicGames]) => {
        const myGameIds = new Set(myGames.map((game) => game.id));
        const otherGames = publicGames.filter((game) => !myGameIds.has(game.id));

        this.orderedGames.set([...myGames, ...otherGames]);
      },
    );

    this.form.controls.fromCurrencyId.valueChanges.subscribe((value) => {
      this.fromCurrencyIdValue.set(value);
      this.form.controls.fromAmount.updateValueAndValidity();
    });

    this.form.controls.targetGameId.valueChanges.subscribe((value) => this.targetGameIdValue.set(value));
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
            this.walletService
              .refreshBalances()
              .subscribe((balances) => this.currencies.set(balances));
          }
        }),
        takeWhile((conversion) => !isTerminalConversionStatus(conversion.status), true),
      )
      .subscribe();
  }
}
