import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { BalanceResponse, WithdrawResult } from './wallet.models';

@Injectable({ providedIn: 'root' })
export class WalletApiService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(private readonly http: HttpClient) {}

  getBalance(walletId: string): Observable<BalanceResponse> {
    return this.http.get<BalanceResponse>(`${this.baseUrl}/wallets/${walletId}/balance`);
  }

  withdraw(walletId: string, amount: number): Observable<WithdrawResult> {
    return this.http.post<WithdrawResult>(`${this.baseUrl}/wallets/${walletId}/withdrawals`, { amount });
  }
}
