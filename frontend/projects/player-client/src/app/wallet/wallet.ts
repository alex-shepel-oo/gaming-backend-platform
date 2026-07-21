import { Component, computed, inject, signal } from '@angular/core';
import { Balance, CurrencyScope, GameSelectionService, TransactionHistoryEntry, WalletService } from 'shared';

const PAGE_SIZE = 20;

@Component({
  selector: 'app-wallet',
  templateUrl: './wallet.html',
})
export class Wallet {
  private readonly walletService = inject(WalletService);
  private readonly gameSelection = inject(GameSelectionService);

  protected readonly CurrencyScope = CurrencyScope;

  protected readonly balances = signal<Balance[]>([]);
  protected readonly transactions = signal<TransactionHistoryEntry[]>([]);
  protected readonly page = signal(1);
  protected readonly totalCount = signal(0);

  protected readonly platformBalances = computed(() =>
    this.balances().filter((balance) => balance.scope === CurrencyScope.Platform),
  );
  protected readonly gameBalances = computed(() =>
    this.balances().filter((balance) => balance.scope === CurrencyScope.Game),
  );
  protected readonly hasNextPage = computed(() => this.page() * PAGE_SIZE < this.totalCount());

  constructor() {
    this.walletService
      .getBalances(this.gameSelection.selected()?.id)
      .subscribe((balances) => this.balances.set(balances));

    this.loadHistory();
  }

  protected nextPage(): void {
    this.page.update((page) => page + 1);
    this.loadHistory();
  }

  private loadHistory(): void {
    this.walletService
      .getTransactionHistory({ page: this.page(), pageSize: PAGE_SIZE })
      .subscribe((result) => {
        this.transactions.set(result.items);
        this.totalCount.set(result.totalCount);
      });
  }
}
