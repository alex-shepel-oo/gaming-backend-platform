import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Subscription, timer } from 'rxjs';
import { switchMap, takeWhile, tap } from 'rxjs/operators';
import {
  Balance,
  Conversion,
  ConversionService,
  ConversionStatus,
  GameSelectionService,
  GamesService,
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
  private readonly gameSelection = inject(GameSelectionService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly ConversionStatus = ConversionStatus;

  protected readonly currencies = signal<Balance[]>([]);
  protected readonly currenciesLoading = signal(true);
  protected readonly currenciesError = signal(false);
  protected readonly gameNames = signal<Map<string, string>>(new Map());

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

  private readonly rawToCurrencyOptions = computed(() => {
    const fromCurrencyId = this.form.controls.fromCurrencyId.value;

    return this.currencies().filter((currency) => currency.currencyId !== fromCurrencyId);
  });

  // Grouping is real and data-driven (currency.gameId), not fabricated -- with
  // today's single-game demo data it always collapses to "no choice needed"
  // and the picker stays hidden. It activates on its own once a second game's
  // currency exists in the player's balances.
  protected readonly toGameOptions = computed(() => {
    const gameIds = new Set(
      this.rawToCurrencyOptions()
        .filter((currency) => currency.gameId !== null)
        .map((currency) => currency.gameId as string),
    );

    return Array.from(gameIds);
  });

  protected readonly showGamePicker = computed(() => this.toGameOptions().length > 1);

  protected readonly toCurrencyOptions = computed(() => {
    if (!this.showGamePicker()) {
      return this.rawToCurrencyOptions();
    }

    const targetGameId = this.form.controls.targetGameId.value;

    return this.rawToCurrencyOptions().filter((currency) => currency.gameId === targetGameId);
  });

  private pollSubscription: Subscription | null = null;

  constructor() {
    this.destroyRef.onDestroy(() => this.pollSubscription?.unsubscribe());

    this.walletService.getBalances(this.gameSelection.selected()?.id).subscribe({
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

    this.gamesService.listPublicGames().subscribe((games) => {
      this.gameNames.set(new Map(games.map((game) => [game.id, game.name])));
    });

    this.form.controls.fromCurrencyId.valueChanges.subscribe(() => {
      this.form.controls.fromAmount.updateValueAndValidity();
    });
  }

  protected gameNameFor(gameId: string): string {
    return this.gameNames().get(gameId) ?? gameId;
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
              .refreshBalances(this.gameSelection.selected()?.id)
              .subscribe((balances) => this.currencies.set(balances));
          }
        }),
        takeWhile((conversion) => !isTerminalConversionStatus(conversion.status), true),
      )
      .subscribe();
  }
}
