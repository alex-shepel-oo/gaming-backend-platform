// Same wire shape as ConversionStatus in EconomyService.Domain.Enums -- a
// plain number, in the order Started/DebitDone/Completed/Compensating/Failed.
export enum ConversionStatus {
  Started = 0,
  DebitDone = 1,
  Completed = 2,
  Compensating = 3,
  Failed = 4,
}

export function isTerminalConversionStatus(status: ConversionStatus): boolean {
  return status === ConversionStatus.Completed || status === ConversionStatus.Failed;
}

export interface Conversion {
  conversionId: string;
  userId: string;
  fromCurrencyId: string;
  toCurrencyId: string;
  fromAmount: number;
  toAmount: number;
  rateApplied: number;
  status: ConversionStatus;
  failureReason: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ConvertRequest {
  fromCurrencyId: string;
  toCurrencyId: string;
  fromAmount: number;
}
