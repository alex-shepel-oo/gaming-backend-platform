import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
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
  NotAvailable,
  PageBackground,
  PublicGame,
  StatusPill,
  StatusPillVariant,
  WalletService,
  isTerminalConversionStatus,
} from 'shared';
import { ConversionTopology } from './conversion-topology';
import { SelectDropdown } from './select-dropdown/select-dropdown';

interface ConvertPreview {
  amount: string;
  code: string;
  rate: number;
}

// The saga's happy path, in order, used to drive the progress stepper.
// Compensating/Failed are exception branches off DebitDone, rendered as a
// status pill instead rather than forced into a dot implying linear progress.
const STEPPER_STATUSES = [ConversionStatus.Started, ConversionStatus.DebitDone, ConversionStatus.Completed];

const POLL_INTERVAL_MS = 1500;

// A whole-number result reads as "100 SHOOTER_GOLD", not "100.00
// SHOOTER_GOLD": the trailing zeros only earn their place once the value
// actually has a fractional part.
function formatAmount(value: number, decimals: number): string {
  return Number.isInteger(value) ? value.toString() : value.toFixed(decimals);
}

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

const STATUS_VARIANTS: Record<ConversionStatus, StatusPillVariant> = {
  [ConversionStatus.Started]: 'progress',
  [ConversionStatus.DebitDone]: 'progress',
  [ConversionStatus.Completed]: 'success',
  [ConversionStatus.Compensating]: 'warning',
  [ConversionStatus.Failed]: 'error',
};

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-convert',
  imports: [
    ReactiveFormsModule,
    MatIconModule,
    MatProgressSpinnerModule,
    NotAvailable,
    PageBackground,
    SelectDropdown,
    StatusPill,
  ],
  templateUrl: './convert.html',
  styleUrls: ['./convert.scss', './convert-status.scss'],
})
export class Convert {
  private readonly formBuilder = inject(FormBuilder);
  private readonly conversionService = inject(ConversionService);
  private readonly walletService = inject(WalletService);
  private readonly gamesService = inject(GamesService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly ConversionStatus = ConversionStatus;

  // Currencies the player actually holds: you can only convert money you
  // have, so "from" stays scoped to balances.
  protected readonly currencies = signal<Balance[]>([]);
  protected readonly currenciesLoading = signal(true);
  protected readonly currenciesError = signal(false);

  // The full currency catalog, independent of held balances. A currency the
  // player has never transacted in (zero balance) must still be reachable as
  // a conversion target, which "from" balances alone cannot offer.
  protected readonly currencyCatalog = signal<Currency[]>([]);

  // Games ordered with the player's own games (listMyGames) first, followed
  // by any other public game (listPublicGames) not already in that set.
  protected readonly orderedGames = signal<PublicGame[]>([]);

  // computed() only tracks signal reads: reading a FormControl's plain
  // .value here would never invalidate the cache when the user picks a new
  // value, so the pickers below react off these signals instead, kept in
  // sync via valueChanges in the constructor.
  private readonly fromCurrencyIdValue = signal('');
  private readonly targetGameIdValue = signal('');
  // Not private: the template reads it directly to decide whether the "To"
  // card has anything to show yet at all.
  protected readonly toCurrencyIdValue = signal('');

  // Conversion-reachability rules (which currencies/games are valid "to"
  // targets given the current "from" pick) live in ConversionTopology, pure
  // derivations of the signals above, testable without a component or
  // TestBed. Must exist before `form` below: FormBuilder runs
  // every validator synchronously while constructing its controls, and
  // those validators (just below) reach into this immediately.
  private readonly topology = new ConversionTopology(
    this.currencyCatalog,
    this.currencies,
    this.orderedGames,
    this.fromCurrencyIdValue,
    this.targetGameIdValue,
  );

  protected readonly toGameOptions = this.topology.toGameOptions;
  protected readonly showGamePicker = this.topology.showGamePicker;
  protected readonly toCurrencyIsChoice = this.topology.toCurrencyIsChoice;
  protected readonly toCurrencyOptions = this.topology.toCurrencyOptions;

  // SelectDropdown takes plain {id, label} options, so these just relabel
  // each signal's own domain objects for it rather than the dropdown
  // component knowing about any of those types.
  protected readonly fromCurrencyDropdownOptions = computed(() =>
    this.currencies().map((currency) => ({ id: currency.currencyId, label: currency.currencyCode })),
  );

  protected readonly targetGameDropdownOptions = computed(() =>
    this.toGameOptions().map((game) => ({ id: game.id, label: game.name })),
  );

  protected readonly toCurrencyDropdownOptions = computed(() =>
    this.toCurrencyOptions().map((currency) => ({ id: currency.id, label: currency.code })),
  );

  private readonly insufficientBalanceValidator: ValidatorFn = (control) =>
    this.topology.insufficientBalanceValidator(control);

  // Re-checked via applyToCurrencyAutoSelect(), called everywhere the
  // candidate set can change (see its own comment).
  private readonly toCurrencyStillOfferedValidator: ValidatorFn = (control) =>
    this.topology.toCurrencyStillOfferedValidator(control);

  private readonly fromCurrencyStillHeldValidator: ValidatorFn = (control) =>
    this.topology.fromCurrencyStillHeldValidator(control);

  // The conversion amount is a whole-number input in the UI (matches the
  // mockup's plain integer stepper), even though EconomyService itself
  // stores amounts as decimal.
  private readonly integerAmountValidator: ValidatorFn = (control) =>
    Number.isInteger(control.value) ? null : { integerAmount: true };

  protected readonly form = this.formBuilder.nonNullable.group({
    fromCurrencyId: ['', [Validators.required, this.fromCurrencyStillHeldValidator]],
    targetGameId: [''],
    toCurrencyId: ['', [Validators.required, this.toCurrencyStillOfferedValidator]],
    fromAmount: [
      0,
      [Validators.required, Validators.min(1), this.integerAmountValidator, this.insufficientBalanceValidator],
    ],
  });

  protected readonly submitting = signal(false);
  protected readonly error = signal<ConvertError | null>(null);
  protected readonly conversion = signal<Conversion | null>(null);

  protected readonly preview = signal<ConvertPreview | null>(null);

  // Drives the "Balance: X available" label next to the From selector,
  // reading off the same held-balances list the selector's own options
  // come from, so it can never disagree with the dropdown.
  protected readonly selectedFromBalance = computed(
    () => this.currencies().find((currency) => currency.currencyId === this.fromCurrencyIdValue()) ?? null,
  );

  // Same idea for the "To" side's "Balance: X CODE" label. Unlike "from",
  // the target currency may be one the player has never held, so this falls
  // back to a zero amount rather than hiding the label entirely.
  protected readonly selectedToBalance = computed(() => {
    const toCurrencyId = this.toCurrencyIdValue();

    if (!toCurrencyId) {
      return null;
    }

    const held = this.currencies().find((currency) => currency.currencyId === toCurrencyId);
    const currency = this.currencyCatalog().find((candidate) => candidate.id === toCurrencyId);

    return { amount: held?.amount ?? 0, currencyCode: currency?.code ?? '' };
  });

  private pollSubscription: Subscription | null = null;

  constructor() {
    this.destroyRef.onDestroy(() => this.pollSubscription?.unsubscribe());

    this.walletService.getBalances().subscribe({
      next: (balances) => {
        this.currencies.set(balances);
        this.currenciesLoading.set(false);
        this.applyFromCurrencyAutoSelect();

        // emitEvent: false on both, since these only need their OWN validators
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

    // Unlike getCurrencies()/listPublicGames() on the Wallet screen, a
    // failure here isn't cosmetic: the currency catalog and game list are
    // what toCurrencyOptions()/toGameOptions() are built from, so losing
    // either leaves the "to" side silently unfillable. Routed into the same
    // currenciesError gate getBalances() already uses, rather than letting
    // the form render half-usable with no explanation.
    this.walletService.getCurrencies().subscribe({
      next: (currencies) => {
        this.currencyCatalog.set(currencies);
        this.applyToCurrencyAutoSelect();
      },
      error: () => this.currenciesError.set(true),
    });

    combineLatest([this.gamesService.listMyGames(), this.gamesService.listPublicGames()]).subscribe({
      next: ([myGames, publicGames]) => {
        const myGameIds = new Set(myGames.map((game) => game.id));
        const otherGames = publicGames.filter((game) => !myGameIds.has(game.id));

        this.orderedGames.set([...myGames, ...otherGames]);
        this.applyTargetGameAutoSelect();
        this.applyToCurrencyAutoSelect();
      },
      error: () => this.currenciesError.set(true),
    });

    this.form.controls.fromCurrencyId.valueChanges.subscribe((value) => {
      this.fromCurrencyIdValue.set(value);
      this.resetConversionState();

      // Starts the player at the smallest valid amount instead of an empty
      // 0 they'd otherwise have to fill in themselves: 1 if they can afford
      // it, otherwise whatever they actually hold.
      const balance = this.currencies().find((currency) => currency.currencyId === value)?.amount ?? 0;
      this.form.controls.fromAmount.setValue(Math.min(1, balance));

      // The set of valid "to" options just changed underneath whatever was
      // previously picked. Resetting targetGameId (always, since setValue
      // still emits even if it was already empty) cascades into the handler
      // below, which resets toCurrencyId in turn and re-runs auto-select
      // against the now-current fromCurrencyId.
      this.form.controls.targetGameId.setValue('');
      this.applyTargetGameAutoSelect();
    });

    this.form.controls.targetGameId.valueChanges.subscribe((value) => {
      this.targetGameIdValue.set(value);
      this.resetConversionState();
      this.form.controls.toCurrencyId.setValue('');
      this.applyToCurrencyAutoSelect();
    });

    this.form.controls.toCurrencyId.valueChanges.subscribe((value) => {
      this.toCurrencyIdValue.set(value);
      this.resetConversionState();
    });

    this.form.controls.fromAmount.valueChanges.subscribe(() => this.resetConversionState());

    this.buildPreviewPipeline();
  }

  // A finished (or failed) conversion's status/stepper is about the specific
  // intent that produced it. Once the player starts shaping a *new* one by
  // touching any input, that stale result no longer describes what they're
  // about to submit.
  private resetConversionState(): void {
    this.pollSubscription?.unsubscribe();
    this.pollSubscription = null;
    this.conversion.set(null);
    this.error.set(null);
  }

  // With no blank placeholder option in the "from" select, the browser would
  // otherwise just display the first real option without the form control
  // agreeing it's actually selected. Picking it here for real keeps the
  // visible state and the control's value in sync from the start.
  private applyFromCurrencyAutoSelect(): void {
    const control = this.form.controls.fromCurrencyId;

    if (!control.value && this.currencies().length > 0) {
      control.setValue(this.currencies()[0].currencyId);
    }
  }

  // Same idea as applyFromCurrencyAutoSelect(), for the (also now
  // placeholder-less) game picker.
  private applyTargetGameAutoSelect(): void {
    const control = this.form.controls.targetGameId;
    const options = this.toGameOptions();

    if (!control.value && options.length > 0) {
      control.setValue(options[0].id);
    }
  }

  // Whenever the live "to" candidate set narrows to exactly one entry, that's
  // no longer a real choice for the user to make, so select it for them.
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

              return {
                amount: formatAmount(fromAmount * rateDto.rate, decimals),
                code: toCurrency?.code ?? '',
                rate: rateDto.rate,
              };
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

  protected statusVariant(status: ConversionStatus): StatusPillVariant {
    return STATUS_VARIANTS[status];
  }

  // Whether this conversion is still on the linear Started -> DebitDone ->
  // Completed line the stepper draws, versus having branched off it into
  // Compensating/Failed (rendered as a status pill instead).
  protected isOnStepperPath(status: ConversionStatus): boolean {
    return STEPPER_STATUSES.includes(status);
  }

  // Drives the spinning sync icon next to the live status word: spins
  // while still short of Completed, stops once the saga lands.
  protected isInProgress(status: ConversionStatus): boolean {
    return this.isOnStepperPath(status) && status !== ConversionStatus.Completed;
  }

  protected stepperStatuses(): ConversionStatus[] {
    return STEPPER_STATUSES;
  }

  protected stepperIndex(status: ConversionStatus): number {
    return STEPPER_STATUSES.indexOf(status);
  }

  protected stepperProgressPercent(status: ConversionStatus): number {
    const index = this.stepperIndex(status);

    return (index / (STEPPER_STATUSES.length - 1)) * 100;
  }

  // The [attr.max] binding alone only clamps the stepper arrows/scroll-wheel:
  // a typed value above the balance still lands in the control as-is, so
  // direct input needs this on top to actually cap it.
  protected clampAmountToBalance(event: Event): void {
    const balance = this.selectedFromBalance()?.amount;

    if (balance === undefined) {
      return;
    }

    const typed = (event.target as HTMLInputElement).valueAsNumber;

    if (!Number.isNaN(typed) && typed > balance) {
      this.form.controls.fromAmount.setValue(balance);
    }
  }

  protected submit(): void {
    if (this.form.invalid) {
      return;
    }

    this.pollSubscription?.unsubscribe();
    this.submitting.set(true);
    this.error.set(null);
    this.conversion.set(null);

    // Generated once per user click ("new intent"), not per HTTP attempt.
    // If this request were ever retried at the network layer, it would
    // reuse this same key rather than call randomUUID() again.
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
        // Composed into the same chain rather than a subscribe() nested inside
        // the tap above: the refresh is a one-shot side effect of landing on
        // Completed, so switchMap here just waits for it (or skips straight
        // through via of()) before takeWhile decides whether polling continues.
        switchMap((conversion) => {
          if (conversion.status !== ConversionStatus.Completed) {
            return of(conversion);
          }

          return this.walletService.refreshBalances().pipe(
            tap((balances) => {
              this.currencies.set(balances);
              this.form.controls.fromCurrencyId.updateValueAndValidity({ emitEvent: false });
            }),
            map(() => conversion),
          );
        }),
        takeWhile((conversion) => !isTerminalConversionStatus(conversion.status), true),
      )
      .subscribe();
  }
}
