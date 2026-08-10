import { signal } from '@angular/core';
import { FormControl, FormGroup } from '@angular/forms';
import { Balance, Currency, CurrencyScope, PublicGame } from 'shared';
import { ConversionTopology } from './conversion-topology';

function currency(id: string, gameId: string | null): Currency {
  return {
    id,
    code: id.toUpperCase(),
    displayName: id,
    scope: gameId ? CurrencyScope.Game : CurrencyScope.Platform,
    gameId,
    decimals: 2,
    iconUrl: null,
  };
}

function balance(currencyId: string, amount: number): Balance {
  return { currencyId, currencyCode: currencyId.toUpperCase(), scope: CurrencyScope.Platform, gameId: null, amount, iconUrl: null };
}

function game(id: string): PublicGame {
  return { id, slug: id, name: id, description: null, iconUrl: null };
}

const platform = currency('code', null);
const shooterGold = currency('shooter-gold', 'shooter');
const racerToken = currency('racer-token', 'racer');

describe('ConversionTopology', () => {
  it('offers nothing before a "from" currency is picked', () => {
    const topology = new ConversionTopology(
      signal([platform, shooterGold, racerToken]),
      signal([]),
      signal([game('shooter'), game('racer')]),
      signal(''),
      signal(''),
    );

    expect(topology.toCurrencyOptions()).toEqual([]);
    expect(topology.toGameOptions()).toEqual([]);
    expect(topology.showGamePicker()).toBe(false);
  });

  it('from the platform currency with more than one reachable game, shows a game picker but no currency options until one is picked', () => {
    const topology = new ConversionTopology(
      signal([platform, shooterGold, racerToken]),
      signal([]),
      signal([game('shooter'), game('racer')]),
      signal('code'),
      signal(''),
    );

    // Ambiguous until a game is picked -- narrowing happens once targetGameId is set (below).
    expect(topology.toCurrencyOptions()).toEqual([]);
    expect(topology.toGameOptions().map((g) => g.id)).toEqual(['shooter', 'racer']);
    expect(topology.showGamePicker()).toBe(true);
  });

  it('from a game currency, the only reachable "to" is the platform currency -- no game picker', () => {
    const topology = new ConversionTopology(
      signal([platform, shooterGold, racerToken]),
      signal([]),
      signal([game('shooter'), game('racer')]),
      signal('shooter-gold'),
      signal(''),
    );

    expect(topology.toCurrencyOptions()).toEqual([platform]);
    expect(topology.toGameOptions()).toEqual([]);
    expect(topology.showGamePicker()).toBe(false);
  });

  it('once a target game is picked (only relevant when the picker is shown), narrows to that game\'s currency', () => {
    const topology = new ConversionTopology(
      signal([platform, shooterGold, racerToken]),
      signal([]),
      signal([game('shooter'), game('racer')]),
      signal('code'),
      signal('racer'),
    );

    // Platform itself isn't a candidate here -- it's the "from" currency being converted.
    expect(topology.toCurrencyOptions()).toEqual([racerToken]);
  });

  it('with only one reachable game, the game currency stays offered without narrowing by targetGameId', () => {
    const topology = new ConversionTopology(
      signal([platform, shooterGold]),
      signal([]),
      signal([game('shooter')]),
      signal('code'),
      signal(''),
    );

    expect(topology.showGamePicker()).toBe(false);
    expect(topology.toCurrencyOptions()).toEqual([shooterGold]);
  });

  it('insufficientBalanceValidator flags an amount above the held balance for the selected "from" currency', () => {
    const topology = new ConversionTopology(
      signal([platform]),
      signal([balance('code', 50)]),
      signal([]),
      signal('code'),
      signal(''),
    );

    const group = new FormGroup({
      fromCurrencyId: new FormControl('code'),
      fromAmount: new FormControl(100),
    });

    expect(topology.insufficientBalanceValidator(group.controls.fromAmount)).toEqual({ insufficientBalance: true });

    group.controls.fromAmount.setValue(50);
    expect(topology.insufficientBalanceValidator(group.controls.fromAmount)).toBeNull();
  });

  it('toCurrencyStillOfferedValidator rejects a value that has fallen out of the live option set', () => {
    const topology = new ConversionTopology(
      signal([platform, shooterGold]),
      signal([]),
      signal([game('shooter')]),
      signal('shooter-gold'),
      signal(''),
    );

    // from shooter-gold, only "code" (platform) is reachable -- "racer-token" is stale.
    const control = new FormControl('racer-token');
    expect(topology.toCurrencyStillOfferedValidator(control)).toEqual({ notOffered: true });

    control.setValue('code');
    expect(topology.toCurrencyStillOfferedValidator(control)).toBeNull();

    control.setValue('');
    expect(topology.toCurrencyStillOfferedValidator(control)).toBeNull();
  });

  it('fromCurrencyStillHeldValidator rejects a currency no longer present in held balances', () => {
    const topology = new ConversionTopology(signal([]), signal([balance('code', 10)]), signal([]), signal(''), signal(''));

    expect(topology.fromCurrencyStillHeldValidator(new FormControl('shooter-gold'))).toEqual({ notHeld: true });
    expect(topology.fromCurrencyStillHeldValidator(new FormControl('code'))).toBeNull();
    expect(topology.fromCurrencyStillHeldValidator(new FormControl(''))).toBeNull();
  });
});
