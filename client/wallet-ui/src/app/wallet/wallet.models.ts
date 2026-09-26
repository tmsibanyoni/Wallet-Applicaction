export interface BalanceResponse {
  walletId: number;
  balance: number;
  currency: string;
}

export interface WithdrawResult {
  withdrawalId: string;
  walletId: number;
  amount: number;
  balanceAfter: number;
  currency: string;
  occurredAtUtc: string;
}

export interface ProblemDetails {
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
}
