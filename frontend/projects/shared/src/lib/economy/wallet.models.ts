// Numeric values mirror the C# enums (EconomyService.Domain.Enums) -- the
// service has no JsonStringEnumConverter configured, so these come over the
// wire as plain numbers, not names.

export enum CurrencyScope {
  Platform = 0,
  Game = 1,
}

export enum TransactionType {
  Grant = 0,
  Spend = 1,
  Adjust = 2,
  ConversionOut = 3,
  ConversionIn = 4,
}

export interface Balance {
  currencyId: string;
  currencyCode: string;
  scope: CurrencyScope;
  gameId: string | null;
  amount: number;
  iconUrl: string | null;
}

// Mirrors EconomyService.Contracts.Responses.CurrencyDto -- the read-only
// currency catalog (every currency across every game, plus platform), not a
// per-user balance. Same numeric-enum-over-the-wire caveat as CurrencyScope
// above: scope comes through as a number, not a name.
export interface Currency {
  id: string;
  code: string;
  displayName: string;
  scope: CurrencyScope;
  gameId: string | null;
  decimals: number;
  iconUrl: string | null;
}

export interface TransactionHistoryEntry {
  id: string;
  currencyId: string;
  amount: number;
  transactionType: TransactionType;
  reason: string | null;
  createdAt: string;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}
