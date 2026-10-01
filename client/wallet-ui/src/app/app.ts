import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, HostListener, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { interval } from 'rxjs';
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
  private readonly destroyRef = inject(DestroyRef);

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

    interval(environment.balanceRefreshIntervalMs)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.refreshBalance());
  }

  @HostListener('document:visibilitychange')
  onVisibilityChange(): void {
    if (document.visibilityState === 'visible') {
      this.refreshBalance();
    }
  }

  /** Quietly re-reads the balance so changes made elsewhere (another tab, another client) show up. */
  refreshBalance(): void {
    if (this.loadingBalance() || this.withdrawing()) {
      return;
    }

    this.walletApi.getBalance(this.walletId).subscribe({
      next: (response) => {
        if (this.withdrawing()) {
          return;
        }
        this.balance.set(response.balance);
        this.currency.set(response.currency);
      },
      error: () => {
        // Keep showing the last known balance; the next tick will try again.
      },
    });
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

    // Full technical detail goes to the console (and any log shipping on top of it) for developers.
    console.error('Wallet API request failed', {
      status: err.status,
      url: err.url,
      traceId: problem?.traceId,
      problem,
      error: err.message,
    });

    if (err.status === 0) {
      return 'We cannot reach the wallet service right now. Please try again in a few minutes.';
    }

    if (err.status >= 500) {
      const reference = problem?.traceId ? ` If it keeps happening, quote reference ${problem.traceId}.` : '';
      return `Something went wrong on our side. Please try again shortly.${reference}`;
    }

    if (problem?.errors) {
      return Object.values(problem.errors).flat().join(' ');
    }

    if (problem?.detail) {
      return problem.detail;
    }

    return 'We could not complete that request. Please check your details and try again.';
  }
}
