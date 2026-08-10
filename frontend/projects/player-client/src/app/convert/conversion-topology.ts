import { Signal, computed } from '@angular/core';
import { ValidatorFn } from '@angular/forms';
import { Balance, Currency, PublicGame } from 'shared';

// Pure conversion-reachability rules, pulled out of Convert so the topology
// (which currencies/games are valid "to" targets given the current "from"
// pick) can be reasoned about -- and unit-tested -- without a component or
// TestBed. Built once per Convert instance from the component's own
// signals; holds no state of its own beyond signals derived from them.
export class ConversionTopology {
  constructor(
    private readonly currencyCatalog: Signal<Currency[]>,
    private readonly heldBalances: Signal<Balance[]>,
    private readonly orderedGames: Signal<PublicGame[]>,
    private readonly fromCurrencyIdValue: Signal<string>,
    private readonly targetGameIdValue: Signal<string>,
  ) {}

  private readonly fromCurrency = computed(
    () => this.currencyCatalog().find((currency) => currency.id === this.fromCurrencyIdValue()) ?? null,
  );

  // A game currency may only ever convert back to the platform currency,
  // never to another game's currency -- there is no rate for that pairing
  // and there never will be per the settled conversion topology.
  private readonly fromIsGameCurrency = computed(() => this.fromCurrency()?.gameId != null);

  private readonly platformCurrency = computed(
    () => this.currencyCatalog().find((currency) => currency.gameId === null) ?? null,
  );

  // Empty until "from" is actually picked -- without this guard, every
  // narrowing rule below (game picker, platform-only fallback) still runs
  // against the full catalog and can spuriously narrow to a single option,
  // auto-selecting a "to" currency before the player has chosen anything.
  private readonly rawToCurrencyOptions = computed(() => {
    const fromCurrencyId = this.fromCurrencyIdValue();

    if (!fromCurrencyId) {
      return [];
    }

    return this.currencyCatalog().filter((currency) => currency.id !== fromCurrencyId);
  });

  // Only games that actually have a reachable currency (i.e. survive the
  // fromCurrencyId exclusion above) are offered, ordered per orderedGames.
  // None are offered at all once "from" is itself a game currency -- there's
  // no game left to pick, the destination is fixed to the platform currency.
  readonly toGameOptions = computed(() => {
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

  readonly showGamePicker = computed(() => !this.fromIsGameCurrency() && this.toGameOptions().length > 1);

  readonly toCurrencyOptions = computed(() => {
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

  // A real choice only exists once there's more than one candidate --
  // otherwise the control just holds whatever the auto-select cascade
  // already picked, and rendering it as an interactive-looking dropdown with
  // a chevron would offer a "choice" the player can't actually make.
  readonly toCurrencyIsChoice = computed(() => this.toCurrencyOptions().length > 1);

  readonly insufficientBalanceValidator: ValidatorFn = (control) => {
    const fromCurrencyId = control.parent?.get('fromCurrencyId')?.value as string | undefined;
    const currency = this.heldBalances().find((candidate) => candidate.currencyId === fromCurrencyId);

    return currency && control.value > currency.amount ? { insufficientBalance: true } : null;
  };

  // A toCurrencyId control can hold a value that was valid a moment ago but
  // no longer is -- e.g. the user picked a game currency as "to", then
  // changed "from" to a game currency itself, which per the conversion
  // topology narrows "to" down to the platform currency only.
  // Validators.required alone can't see that: the control still has a
  // non-empty value, it's just not a live option anymore.
  readonly toCurrencyStillOfferedValidator: ValidatorFn = (control) => {
    const value = control.value as string;

    if (!value) {
      return null;
    }

    return this.toCurrencyOptions().some((currency) => currency.id === value) ? null : { notOffered: true };
  };

  // Same staleness concern as above, but for "from": it's built from held
  // balances, which can change out from under a previous selection (e.g.
  // after a conversion drains a balance to zero).
  readonly fromCurrencyStillHeldValidator: ValidatorFn = (control) => {
    const value = control.value as string;

    if (!value) {
      return null;
    }

    return this.heldBalances().some((currency) => currency.currencyId === value) ? null : { notHeld: true };
  };
}
