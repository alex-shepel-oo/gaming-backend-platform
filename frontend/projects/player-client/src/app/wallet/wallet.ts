import { Component, computed, inject, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import {
  Balance,
  CurrencyScope,
  GameSelectionService,
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

@Component({
  selector: 'app-wallet',
  imports: [MatCardModule, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './wallet.html',
  styleUrl: './wallet.scss',
})
export class Wallet {
  private readonly walletService = inject(WalletService);
  private readonly gameSelection = inject(GameSelectionService);

  protected readonly CurrencyScope = CurrencyScope;

  protected readonly balances = signal<Balance[]>([]);
  protected readonly balancesLoading = signal(true);
  protected readonly balancesError = signal(false);

  protected readonly transactions = signal<TransactionHistoryEntry[]>([]);
  protected readonly historyLoading = signal(true);
  protected readonly historyError = signal(false);

  protected readonly page = signal(1);
  protected readonly totalCount = signal(0);

  protected readonly platformBalances = computed(() =>
    this.balances().filter((balance) => balance.scope === CurrencyScope.Platform),
  );
  protected readonly gameBalances = computed(() =>
    this.balances().filter((balance) => balance.scope === CurrencyScope.Game),
  );
  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)));
  protected readonly hasNextPage = computed(() => this.page() * PAGE_SIZE < this.totalCount());
  protected readonly hasPreviousPage = computed(() => this.page() > 1);

  constructor() {
    this.walletService.getBalances(this.gameSelection.selected()?.id).subscribe({
      next: (balances) => {
        this.balances.set(balances);
        this.balancesLoading.set(false);
      },
      error: () => {
        this.balancesLoading.set(false);
        this.balancesError.set(true);
      },
    });

    this.loadHistory();
  }

  protected transactionLabel(transaction: TransactionHistoryEntry): string {
    return TRANSACTION_TYPE_LABELS[transaction.transactionType];
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
