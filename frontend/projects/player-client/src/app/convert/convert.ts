import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Subscription, timer } from 'rxjs';
import { switchMap, takeWhile, tap } from 'rxjs/operators';
import { Conversion, ConversionService, ConversionStatus, isTerminalConversionStatus } from 'shared';

const POLL_INTERVAL_MS = 1500;

type ConvertError = 'insufficient-funds' | 'conflict';

function classifyConvertError(error: unknown): ConvertError {
  if (error instanceof HttpErrorResponse && error.status === 402) {
    return 'insufficient-funds';
  }

  return 'conflict';
}

@Component({
  selector: 'app-convert',
  imports: [ReactiveFormsModule],
  templateUrl: './convert.html',
})
export class Convert {
  private readonly formBuilder = inject(FormBuilder);
  private readonly conversionService = inject(ConversionService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly ConversionStatus = ConversionStatus;

  protected readonly form = this.formBuilder.nonNullable.group({
    fromCurrencyId: ['', Validators.required],
    toCurrencyId: ['', Validators.required],
    fromAmount: [0, [Validators.required, Validators.min(0.01)]],
  });

  protected readonly submitting = signal(false);
  protected readonly error = signal<ConvertError | null>(null);
  protected readonly conversion = signal<Conversion | null>(null);

  private pollSubscription: Subscription | null = null;

  constructor() {
    this.destroyRef.onDestroy(() => this.pollSubscription?.unsubscribe());
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
