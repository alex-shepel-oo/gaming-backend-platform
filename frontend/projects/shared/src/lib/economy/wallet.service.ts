import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { EconomyEndpoints } from './economy-endpoints';
import { Balance, PagedResult, TransactionHistoryEntry } from './wallet.models';

export interface TransactionHistoryQuery {
  page: number;
  pageSize: number;
  currencyId?: string;
}

@Injectable({ providedIn: 'root' })
export class WalletService {
  private readonly http = inject(HttpClient);

  // Cross-screen snapshot (shell toolbar, Convert) so navigating between
  // screens doesn't each independently re-fetch the same balances -- Wallet
  // itself keeps its own loading/error signals built on getBalances() below,
  // this is additive, not a replacement.
  private readonly balancesSignal = signal<Balance[] | null>(null);
  readonly balances = this.balancesSignal.asReadonly();

  refreshBalances(gameId?: string): Observable<Balance[]> {
    return this.getBalances(gameId).pipe(tap((balances) => this.balancesSignal.set(balances)));
  }

  clearBalances(): void {
    this.balancesSignal.set(null);
  }

  applyBalanceChange(currencyId: string, balance: number): void {
    this.balancesSignal.update((balances) =>
      balances?.map((b) => (b.currencyId === currencyId ? { ...b, amount: balance } : b)) ?? balances,
    );
  }

  getBalances(gameId?: string): Observable<Balance[]> {
    const params = gameId ? new HttpParams().set('gameId', gameId) : undefined;

    return this.http.get<Balance[]>(EconomyEndpoints.balances, { params });
  }

  getTransactionHistory(query: TransactionHistoryQuery): Observable<PagedResult<TransactionHistoryEntry>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);

    if (query.currencyId) {
      params = params.set('currencyId', query.currencyId);
    }

    return this.http.get<PagedResult<TransactionHistoryEntry>>(EconomyEndpoints.transactions, { params });
  }
}
