export interface BalanceResponse {
  walletId: string;
  balance: number;
  currency: string;
}

export interface WithdrawResult {
  withdrawalId: string;
  walletId: string;
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
