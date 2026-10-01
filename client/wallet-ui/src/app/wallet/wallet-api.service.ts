import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { BalanceResponse, WithdrawResult, WithdrawalSummary } from './wallet.models';

@Injectable({ providedIn: 'root' })
export class WalletApiService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(private readonly http: HttpClient) {}

  getBalance(walletId: string): Observable<BalanceResponse> {
    return this.http.get<BalanceResponse>(`${this.baseUrl}/wallets/${walletId}/balance`);
  }

  /** The same idempotency key always yields the same withdrawal, so a retry can never take the money twice. */
  withdraw(walletId: string, amount: number, idempotencyKey: string): Observable<WithdrawResult> {
    return this.http.post<WithdrawResult>(
      `${this.baseUrl}/wallets/${walletId}/withdrawals`,
      { amount },
      { headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }) },
    );
  }

  getWithdrawals(walletId: string, limit: number): Observable<WithdrawalSummary[]> {
    return this.http.get<WithdrawalSummary[]>(`${this.baseUrl}/wallets/${walletId}/withdrawals`, {
      params: { limit },
    });
  }
}
