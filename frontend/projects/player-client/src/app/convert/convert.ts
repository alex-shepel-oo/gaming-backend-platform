import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
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
  WalletService,
  isTerminalConversionStatus,
} from 'shared';

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
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './convert.html',
  styleUrl: './convert.scss',
})
export class Convert {
  private readonly formBuilder = inject(FormBuilder);
  private readonly conversionService = inject(ConversionService);
  private readonly walletService = inject(WalletService);
  private readonly gameSelection = inject(GameSelectionService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly ConversionStatus = ConversionStatus;

  protected readonly form = this.formBuilder.nonNullable.group({
    fromCurrencyId: ['', Validators.required],
    toCurrencyId: ['', Validators.required],
    fromAmount: [0, [Validators.required, Validators.min(0.01)]],
  });

  protected readonly currencies = signal<Balance[]>([]);
  protected readonly currenciesLoading = signal(true);
  protected readonly currenciesError = signal(false);

  protected readonly submitting = signal(false);
  protected readonly error = signal<ConvertError | null>(null);
  protected readonly conversion = signal<Conversion | null>(null);

  protected readonly toCurrencyOptions = computed(() => {
    const fromCurrencyId = this.form.controls.fromCurrencyId.value;

    return this.currencies().filter((currency) => currency.currencyId !== fromCurrencyId);
  });

  private pollSubscription: Subscription | null = null;

  constructor() {
    this.destroyRef.onDestroy(() => this.pollSubscription?.unsubscribe());

    this.walletService.getBalances(this.gameSelection.selected()?.id).subscribe({
      next: (balances) => {
        this.currencies.set(balances);
        this.currenciesLoading.set(false);
      },
      error: () => {
        this.currenciesLoading.set(false);
        this.currenciesError.set(true);
      },
    });
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
        tap((conversion) => this.conversion.set(conversion)),
        takeWhile((conversion) => !isTerminalConversionStatus(conversion.status), true),
      )
      .subscribe();
  }
}
