import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
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
