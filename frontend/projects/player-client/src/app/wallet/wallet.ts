import { Component, computed, inject, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import {
  Balance,
  CurrencyScope,
  GamesService,
  TransactionHistoryEntry,
  TransactionType,
  WalletService,
} from 'shared';

const PAGE_SIZE = 20;

const TRANSACTION_TYPE_LABELS: Record<TransactionType, string> = {
  [TransactionType.Grant]: 'Grant',
  [TransactionType.Spend]: 'Spend',
  [TransactionType.Adjust]: 'Adjustment',
  [TransactionType.ConversionOut]: 'Conversion out',
  [TransactionType.ConversionIn]: 'Conversion in',
};

interface GameBalanceGroup {
  gameId: string;
  gameName: string;
  balances: Balance[];
}

@Component({
  selector: 'app-wallet',
  imports: [MatCardModule, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './wallet.html',
  styleUrl: './wallet.scss',
})
export class Wallet {
  private readonly walletService = inject(WalletService);
  private readonly gamesService = inject(GamesService);

  protected readonly CurrencyScope = CurrencyScope;

  protected readonly balances = signal<Balance[]>([]);
  protected readonly balancesLoading = signal(true);
  protected readonly balancesError = signal(false);
  protected readonly gameNames = signal<Map<string, string>>(new Map());

  protected readonly currencyCodes = signal<Map<string, string>>(new Map());

  protected readonly transactions = signal<TransactionHistoryEntry[]>([]);
  protected readonly historyLoading = signal(true);
  protected readonly historyError = signal(false);

  protected readonly page = signal(1);
  protected readonly totalCount = signal(0);

  protected readonly platformBalances = computed(() =>
    this.balances().filter((balance) => balance.scope === CurrencyScope.Platform),
  );

  // One card per game the player holds a balance in, rather than a single
  // undifferentiated "in-game" list -- balances can now span several games
  // at once.
  protected readonly gameBalanceGroups = computed<GameBalanceGroup[]>(() => {
    const groups = new Map<string, Balance[]>();

    for (const balance of this.balances()) {
      if (balance.scope !== CurrencyScope.Game || balance.gameId === null) {
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

  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)));
  protected readonly hasNextPage = computed(() => this.page() * PAGE_SIZE < this.totalCount());
  protected readonly hasPreviousPage = computed(() => this.page() > 1);

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

    this.walletService.getCurrencies().subscribe((currencies) => {
      this.currencyCodes.set(new Map(currencies.map((currency) => [currency.id, currency.code])));
    });

    this.gamesService.listPublicGames().subscribe((games) => {
      this.gameNames.set(new Map(games.map((game) => [game.id, game.name])));
    });

    this.loadHistory();
  }

  protected transactionLabel(transaction: TransactionHistoryEntry): string {
    return TRANSACTION_TYPE_LABELS[transaction.transactionType];
  }

  protected transactionCurrencyCode(transaction: TransactionHistoryEntry): string {
    return this.currencyCodes().get(transaction.currencyId) ?? transaction.currencyId;
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

    this.walletService.getTransactionHistory({ page: this.page(), pageSize: PAGE_SIZE }).subscribe({
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
