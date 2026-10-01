import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { environment } from '../environments/environment';
import { WalletApiService } from './wallet/wallet-api.service';
import { ProblemDetails } from './wallet/wallet.models';

@Component({
  selector: 'app-root',
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App implements OnInit {
  private readonly walletApi = inject(WalletApiService);

  walletId = environment.defaultWalletId;
  balance = signal<number | null>(null);
  currency = signal<string>('');
  withdrawAmount: number | null = null;

  loadingBalance = signal(false);
  withdrawing = signal(false);
  errorMessage = signal<string | null>(null);
  successMessage = signal<string | null>(null);

  formatMoney(amount: number, currency: string): string {
    return new Intl.NumberFormat(environment.locale, { style: 'currency', currency }).format(amount);
  }

  ngOnInit(): void {
    this.loadBalance();
  }

  loadBalance(): void {
    this.errorMessage.set(null);
    this.loadingBalance.set(true);

    this.walletApi.getBalance(this.walletId).subscribe({
      next: (response) => {
        this.balance.set(response.balance);
        this.currency.set(response.currency);
        this.loadingBalance.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.balance.set(null);
        this.errorMessage.set(this.describeError(err));
        this.loadingBalance.set(false);
      },
    });
  }

  withdraw(): void {
    if (!this.withdrawAmount || this.withdrawAmount <= 0) {
      this.errorMessage.set('Enter an amount greater than zero.');
      return;
    }

    this.errorMessage.set(null);
    this.successMessage.set(null);
    this.withdrawing.set(true);

    this.walletApi.withdraw(this.walletId, this.withdrawAmount).subscribe({
      next: (result) => {
        this.balance.set(result.balanceAfter);
        this.currency.set(result.currency);
        this.successMessage.set(`Withdrew ${this.formatMoney(result.amount, result.currency)}. New balance: ${this.formatMoney(result.balanceAfter, result.currency)}.`);
        this.withdrawAmount = null;
        this.withdrawing.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.errorMessage.set(this.describeError(err));
        this.withdrawing.set(false);
      },
    });
  }

  private describeError(err: HttpErrorResponse): string {
    const problem = err.error as ProblemDetails | undefined;

    if (problem?.errors) {
      return Object.values(problem.errors).flat().join(' ');
    }

    if (problem?.detail) {
      return problem.detail;
    }

    if (err.status === 0) {
      return 'Could not reach the Wallet API. Is it running on ' + environment.apiBaseUrl + '?';
    }

    return `Request failed (${err.status}).`;
  }
}
