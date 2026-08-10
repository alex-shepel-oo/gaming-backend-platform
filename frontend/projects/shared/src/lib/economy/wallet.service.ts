import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { EconomyEndpoints } from './economy-endpoints';
import { Balance, Currency, PagedResult, TransactionHistoryEntry, TransactionType } from './wallet.models';

export interface TransactionHistoryQuery {
  page: number;
  pageSize: number;
  currencyId?: string;
  types?: TransactionType[];
}

@Injectable({ providedIn: 'root' })
export class WalletService {
  private readonly http = inject(HttpClient);

  // Cross-screen snapshot (shell toolbar, Convert) so navigating between
  // screens doesn't each independently re-fetch the same balances. Wallet
  // itself keeps its own loading/error signals built on getBalances() below;
  // this is additive, not a replacement.
  private readonly balancesSignal = signal<Balance[] | null>(null);
  readonly balances = this.balancesSignal.asReadonly();

  refreshBalances(): Observable<Balance[]> {
    return this.getBalances().pipe(tap((balances) => this.balancesSignal.set(balances)));
  }

  clearBalances(): void {
    this.balancesSignal.set(null);
  }

  applyBalanceChange(currencyId: string, balance: number): void {
    const balances = this.balancesSignal();

    // A currency absent from the cached snapshot means it didn't exist yet
    // the last time it was fetched (e.g. the welcome grant that creates a
    // player's very first balance lands after Shell's initial refresh).
    // The push only carries currencyId/amount/balance, not enough to build
    // a full Balance entry, so the only way to pick up a brand-new currency
    // is a real refetch rather than patching the cached array in place.
    if (!balances?.some((b) => b.currencyId === currencyId)) {
      this.refreshBalances().subscribe();
      return;
    }

    this.balancesSignal.set(balances.map((b) => (b.currencyId === currencyId ? { ...b, amount: balance } : b)));
  }

  getBalances(): Observable<Balance[]> {
    return this.http.get<Balance[]>(EconomyEndpoints.balances);
  }

  getCurrencies(): Observable<Currency[]> {
    return this.http.get<Currency[]>(EconomyEndpoints.currencies);
  }

  getTransactionHistory(query: TransactionHistoryQuery): Observable<PagedResult<TransactionHistoryEntry>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);

    if (query.currencyId) {
      params = params.set('currencyId', query.currencyId);
    }

    for (const type of query.types ?? []) {
      params = params.append('types', type);
    }

    return this.http.get<PagedResult<TransactionHistoryEntry>>(EconomyEndpoints.transactions, { params });
  }
}
