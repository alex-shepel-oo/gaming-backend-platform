import { DatePipe, LowerCasePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import {
  Balance,
  CurrencyScope,
  GamesService,
  PageBackground,
  TransactionHistoryEntry,
  TransactionType,
  WalletService,
} from 'shared';

const PAGE_SIZE = 20;
const PAGE_WINDOW = 2;

// Matches the idempotency key shape ConversionSaga writes: "conversion:{id}:debit"
// or "conversion:{id}:credit" (see EconomyService's ConversionSaga.DebitAsync/
// CreditAsync). ":compensation" deliberately doesn't match: a compensating
// refund posts as a plain Grant back to the original currency, not a second
// leg of the conversion, so it stays a single row rather than being paired.
const CONVERSION_LEG_PATTERN = /^conversion:(.+):(debit|credit)$/;

const TRANSACTION_TYPE_LABELS: Record<TransactionType, string> = {
  [TransactionType.Grant]: 'Grant',
  [TransactionType.Spend]: 'Spend',
  [TransactionType.Adjust]: 'Adjustment',
  [TransactionType.ConversionOut]: 'Conversion out',
  [TransactionType.ConversionIn]: 'Conversion in',
};

// Icon + accent per type, matching the mockup's colored-circle treatment:
// success/green for money in, neutral for a player's own spend (not a
// failure, so not error-red), warning/amber for a system adjustment, and the
// swap icon for both conversion legs since they're the same underlying move.
const TRANSACTION_TYPE_ICON: Record<TransactionType, string> = {
  [TransactionType.Grant]: 'download',
  [TransactionType.Spend]: 'shopping_cart',
  [TransactionType.Adjust]: 'build',
  [TransactionType.ConversionOut]: 'swap_horiz',
  [TransactionType.ConversionIn]: 'swap_horiz',
};

// The icon circle reads richer than the amount text does: a spend gets a
// tinted-red icon (it draws attention as an outgoing transaction) but its
// amount stays plain on-surface, not error-red: spending isn't a failure,
// only a genuine error state should read as one.
const TRANSACTION_TYPE_ICON_VARIANT: Record<TransactionType, 'success' | 'warning' | 'error' | 'neutral'> = {
  [TransactionType.Grant]: 'success',
  [TransactionType.Spend]: 'error',
  [TransactionType.Adjust]: 'warning',
  [TransactionType.ConversionOut]: 'neutral',
  [TransactionType.ConversionIn]: 'neutral',
};

const TRANSACTION_TYPE_AMOUNT_VARIANT: Record<TransactionType, 'success' | 'warning' | 'neutral'> = {
  [TransactionType.Grant]: 'success',
  [TransactionType.Spend]: 'neutral',
  [TransactionType.Adjust]: 'warning',
  [TransactionType.ConversionOut]: 'neutral',
  [TransactionType.ConversionIn]: 'success',
};

type HistoryFilter = 'all' | 'grants' | 'spends' | 'adjustments' | 'conversions';

const HISTORY_FILTER_LABELS: Record<HistoryFilter, string> = {
  all: 'All Types',
  grants: 'Grants',
  spends: 'Spends',
  adjustments: 'Adjustments',
  conversions: 'Conversions',
};

const HISTORY_FILTER_TYPES: Record<HistoryFilter, TransactionType[] | undefined> = {
  all: undefined,
  grants: [TransactionType.Grant],
  spends: [TransactionType.Spend],
  adjustments: [TransactionType.Adjust],
  conversions: [TransactionType.ConversionOut, TransactionType.ConversionIn],
};

const HISTORY_FILTERS: HistoryFilter[] = ['all', 'grants', 'conversions', 'spends', 'adjustments'];

interface GameBalanceGroup {
  gameId: string;
  gameName: string;
  balances: Balance[];
}

interface ConversionRow {
  kind: 'conversion';
  id: string;
  createdAt: string;
  fromEntry: TransactionHistoryEntry;
  toEntry: TransactionHistoryEntry;
}

interface SingleRow {
  kind: 'single';
  id: string;
  entry: TransactionHistoryEntry;
}

type HistoryRow = ConversionRow | SingleRow;

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-wallet',
  imports: [DatePipe, LowerCasePipe, MatIconModule, MatProgressSpinnerModule, PageBackground, RouterLink],
  templateUrl: './wallet.html',
  styleUrls: ['./wallet.scss', './wallet-history.scss'],
})
export class Wallet {
  private readonly walletService = inject(WalletService);
  private readonly gamesService = inject(GamesService);

  protected readonly CurrencyScope = CurrencyScope;
  protected readonly historyFilters = HISTORY_FILTERS;

  protected readonly balances = signal<Balance[]>([]);
  protected readonly balancesLoading = signal(true);
  protected readonly balancesError = signal(false);
  protected readonly gameNames = signal<Map<string, string>>(new Map());

  protected readonly currencyCodes = signal<Map<string, string>>(new Map());

  protected readonly transactions = signal<TransactionHistoryEntry[]>([]);
  protected readonly historyLoading = signal(true);
  protected readonly historyError = signal(false);
  protected readonly historyFilter = signal<HistoryFilter>('all');

  protected readonly page = signal(1);
  protected readonly totalCount = signal(0);

  protected readonly platformBalances = computed(() =>
    this.balances().filter((balance) => balance.scope === CurrencyScope.Platform),
  );

  // A currencyId lands here once its iconUrl 404s/CORS-fails/etc. The
  // broken-image glyph the browser shows by default looks far worse than
  // just falling back to a plain icon, and there's no way to know an image
  // won't load ahead of the request.
  protected readonly failedIcons = signal<ReadonlySet<string>>(new Set());

  protected onIconError(currencyId: string): void {
    this.failedIcons.update((failed) => new Set(failed).add(currencyId));
  }

  // One card per game the player holds a balance in, rather than a single
  // undifferentiated "in-game" list, since balances can now span several
  // games at once. Zero-amount balances are dropped entirely: a currency
  // the player doesn't actually hold isn't worth a line here, and a game
  // left with none at all drops out of the list rather than showing an
  // empty card.
  protected readonly gameBalanceGroups = computed<GameBalanceGroup[]>(() => {
    const groups = new Map<string, Balance[]>();

    for (const balance of this.balances()) {
      if (balance.scope !== CurrencyScope.Game || balance.gameId === null || balance.amount <= 0) {
        continue;
      }

      const existing = groups.get(balance.gameId);
      if (existing) {
        existing.push(balance);
      } else {
        groups.set(balance.gameId, [balance]);
      }
    }

    return Array.from(groups.entries()).map(([gameId, groupBalances]) => ({
      gameId,
      gameName: this.gameNames().get(gameId) ?? gameId,
      balances: groupBalances,
    }));
  });

  // Folds a ConversionOut/ConversionIn pair sharing the same idempotency-key
  // conversion id into one combined row, only when *both* legs landed on
  // the currently fetched page. A pair split across a page boundary (rare:
  // the two legs post moments apart, so they're adjacent in CreatedAt order)
  // just falls back to two ordinary single rows instead of a broken pairing.
  protected readonly historyRows = computed<HistoryRow[]>(() => {
    const entries = this.transactions();
    const legs = new Map<string, { debit?: TransactionHistoryEntry; credit?: TransactionHistoryEntry }>();

    for (const entry of entries) {
      const match = entry.idempotencyKey?.match(CONVERSION_LEG_PATTERN);
      if (!match) {
        continue;
      }

      const bucket = legs.get(match[1]) ?? {};
      if (match[2] === 'debit') {
        bucket.debit = entry;
      } else {
        bucket.credit = entry;
      }
      legs.set(match[1], bucket);
    }

    const consumed = new Set<string>();
    const rows: HistoryRow[] = [];

    for (const entry of entries) {
      if (consumed.has(entry.id)) {
        continue;
      }

      const conversionId = entry.idempotencyKey?.match(CONVERSION_LEG_PATTERN)?.[1];
      const bucket = conversionId ? legs.get(conversionId) : undefined;

      if (bucket?.debit && bucket?.credit) {
        consumed.add(bucket.debit.id);
        consumed.add(bucket.credit.id);
        rows.push({
          kind: 'conversion',
          id: conversionId!,
          createdAt: bucket.debit.createdAt,
          fromEntry: bucket.debit,
          toEntry: bucket.credit,
        });
        continue;
      }

      rows.push({ kind: 'single', id: entry.id, entry });
    }

    return rows;
  });

  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)));
  protected readonly hasNextPage = computed(() => this.page() * PAGE_SIZE < this.totalCount());
  protected readonly hasPreviousPage = computed(() => this.page() > 1);

  // A small window of page numbers around the current one, not every page --
  // matches the mockup's numbered pagination without needing an ellipsis
  // component for a page count that, in practice, stays small.
  protected readonly pageWindow = computed(() => {
    const total = this.totalPages();
    const current = this.page();
    const start = Math.max(1, current - PAGE_WINDOW);
    const end = Math.min(total, current + PAGE_WINDOW);

    return Array.from({ length: end - start + 1 }, (_, i) => start + i);
  });

  constructor() {
    this.walletService.getBalances().subscribe({
      next: (balances) => {
        this.balances.set(balances);
        this.balancesLoading.set(false);
      },
      error: () => {
        this.balancesLoading.set(false);
        this.balancesError.set(true);
      },
    });

    // Deliberately silent on failure, unlike getBalances()/loadHistory() above --
    // these two only feed cosmetic label lookups (transactionCurrencyCode,
    // gameBalanceGroups both already fall back to the raw id), so a failure
    // here degrades a label, not the page. error: is still explicit so this
    // reads as a decision, not a missed case.
    this.walletService.getCurrencies().subscribe({
      next: (currencies) => this.currencyCodes.set(new Map(currencies.map((currency) => [currency.id, currency.code]))),
      error: () => {},
    });

    this.gamesService.listPublicGames().subscribe({
      next: (games) => this.gameNames.set(new Map(games.map((game) => [game.id, game.name]))),
      error: () => {},
    });

    this.loadHistory();
  }

  protected transactionLabel(transaction: TransactionHistoryEntry): string {
    return TRANSACTION_TYPE_LABELS[transaction.transactionType];
  }

  protected transactionIcon(transaction: TransactionHistoryEntry): string {
    return TRANSACTION_TYPE_ICON[transaction.transactionType];
  }

  protected transactionIconVariant(transaction: TransactionHistoryEntry): 'success' | 'warning' | 'error' | 'neutral' {
    return TRANSACTION_TYPE_ICON_VARIANT[transaction.transactionType];
  }

  protected transactionAmountVariant(transaction: TransactionHistoryEntry): 'success' | 'warning' | 'neutral' {
    return TRANSACTION_TYPE_AMOUNT_VARIANT[transaction.transactionType];
  }

  protected transactionCurrencyCode(transaction: TransactionHistoryEntry): string {
    return this.currencyCodes().get(transaction.currencyId) ?? transaction.currencyId;
  }

  protected filterLabel(filter: HistoryFilter): string {
    return HISTORY_FILTER_LABELS[filter];
  }

  protected setFilter(filter: HistoryFilter): void {
    if (filter === this.historyFilter()) {
      return;
    }

    this.historyFilter.set(filter);
    this.page.set(1);
    this.loadHistory();
  }

  protected goToPage(page: number): void {
    if (page === this.page()) {
      return;
    }

    this.page.set(page);
    this.loadHistory();
  }

  protected nextPage(): void {
    if (!this.hasNextPage()) {
      return;
    }

    this.page.update((page) => page + 1);
    this.loadHistory();
  }

  protected previousPage(): void {
    if (!this.hasPreviousPage()) {
      return;
    }

    this.page.update((page) => page - 1);
    this.loadHistory();
  }

  private loadHistory(): void {
    this.historyLoading.set(true);
    this.historyError.set(false);

    this.walletService
      .getTransactionHistory({
        page: this.page(),
        pageSize: PAGE_SIZE,
        types: HISTORY_FILTER_TYPES[this.historyFilter()],
      })
      .subscribe({
        next: (result) => {
          this.transactions.set(result.items);
          this.totalCount.set(result.totalCount);
          this.historyLoading.set(false);
        },
        error: () => {
          this.historyLoading.set(false);
          this.historyError.set(true);
        },
      });
  }
}
