const ECONOMY_BASE_PATH = '/api/economy';

export const EconomyEndpoints = {
  balances: `${ECONOMY_BASE_PATH}/balances/me`,
  currencies: `${ECONOMY_BASE_PATH}/currencies`,
  transactions: `${ECONOMY_BASE_PATH}/transactions/me`,
  conversions: `${ECONOMY_BASE_PATH}/conversions`,
  conversion: (conversionId: string): string => `${ECONOMY_BASE_PATH}/conversions/${conversionId}`,
  conversionRate: `${ECONOMY_BASE_PATH}/conversions/rate`,
} as const;
