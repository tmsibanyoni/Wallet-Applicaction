import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, HostListener, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { interval } from 'rxjs';
import { environment } from '../environments/environment';
import { WalletApiService } from './wallet/wallet-api.service';
import { ProblemDetails, WithdrawalSummary } from './wallet/wallet.models';

interface PendingWithdrawal {
  walletId: string;
  amount: number;
  currency: string;
  /** Sent with every attempt for this confirmation, so a retry cannot withdraw twice. */
  idempotencyKey: string;
}

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
  pendingWithdrawal = signal<PendingWithdrawal | null>(null);
  history = signal<WithdrawalSummary[]>([]);
  showHistory = signal(false);
  errorMessage = signal<string | null>(null);
  successMessage = signal<string | null>(null);

  formatMoney(amount: number, currency: string): string {
    return new Intl.NumberFormat(environment.locale, { style: 'currency', currency }).format(amount);
  }

  /** What the balance will be once the pending withdrawal goes through, based on the latest balance. */
  balanceAfterPending(): number | null {
    const pending = this.pendingWithdrawal();
    const balance = this.balance();
    return pending && balance !== null ? Math.round((balance - pending.amount) * 100) / 100 : null;
  }

  canConfirmWithdrawal(): boolean {
    const after = this.balanceAfterPending();
    return after !== null && after >= 0 && !this.withdrawing();
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

  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.cancelWithdrawal();
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
        const changed = response.balance !== this.balance();
        this.balance.set(response.balance);
        this.currency.set(response.currency);
        if (changed) {
          this.loadHistory();
        }
      },
      error: () => {
        // Keep showing the last known balance; the next tick will try again.
      },
    });
  }

  loadBalance(): void {
    this.errorMessage.set(null);
    this.pendingWithdrawal.set(null);
    this.loadingBalance.set(true);

    this.walletApi.getBalance(this.walletId).subscribe({
      next: (response) => {
        this.balance.set(response.balance);
        this.currency.set(response.currency);
        this.loadingBalance.set(false);
        this.loadHistory();
      },
      error: (err: HttpErrorResponse) => {
        this.balance.set(null);
        this.history.set([]);
        this.errorMessage.set(this.describeError(err));
        this.loadingBalance.set(false);
      },
    });
  }

  toggleHistory(): void {
    this.showHistory.update((open) => !open);
  }

  /** Reads the recent withdrawals; the list is a convenience, so a failure just leaves the old one showing. */
  loadHistory(): void {
    const walletId = this.walletId;

    this.walletApi.getWithdrawals(walletId, environment.historyPageSize).subscribe({
      next: (items) => {
        if (walletId === this.walletId) {
          this.history.set(items);
        }
      },
      error: (err: HttpErrorResponse) => console.error('Could not load withdrawal history', { status: err.status, url: err.url }),
    });
  }

  /** Step one: check the entry and ask the user to confirm. Nothing is sent to the API yet. */
  requestWithdrawal(): void {
    this.successMessage.set(null);

    const amount = this.withdrawAmount;
    const balance = this.balance();

    if (!amount || amount <= 0) {
      this.errorMessage.set('Enter an amount greater than zero.');
      return;
    }

    if (Math.round(amount * 100) / 100 !== amount) {
      this.errorMessage.set('Enter an amount with at most two decimal places.');
      return;
    }

    if (balance === null) {
      this.errorMessage.set('Load the wallet before withdrawing.');
      return;
    }

    if (amount > balance) {
      this.errorMessage.set(`You can withdraw up to ${this.formatMoney(balance, this.currency())}.`);
      return;
    }

    this.errorMessage.set(null);
    this.pendingWithdrawal.set({
      walletId: this.walletId,
      amount,
      currency: this.currency(),
      idempotencyKey: crypto.randomUUID(),
    });
    setTimeout(() => document.getElementById('cancel-withdrawal')?.focus());
  }

  cancelWithdrawal(): void {
    if (this.withdrawing()) {
      return;
    }
    this.pendingWithdrawal.set(null);
  }

  /** Step two: the user has confirmed, so send the withdrawal. */
  confirmWithdrawal(): void {
    const pending = this.pendingWithdrawal();
    if (!pending || !this.canConfirmWithdrawal()) {
      return;
    }

    this.errorMessage.set(null);
    this.successMessage.set(null);
    this.withdrawing.set(true);

    this.walletApi.withdraw(pending.walletId, pending.amount, pending.idempotencyKey).subscribe({
      next: (result) => {
        this.balance.set(result.balanceAfter);
        this.currency.set(result.currency);
        const amount = this.formatMoney(result.amount, result.currency);
        const balanceAfter = this.formatMoney(result.balanceAfter, result.currency);
        this.successMessage.set(
          result.replayed
            ? `That withdrawal of ${amount} had already gone through, so nothing was taken twice. Balance after it: ${balanceAfter}.`
            : `Withdrew ${amount}. New balance: ${balanceAfter}.`,
        );
        this.withdrawAmount = null;
        this.pendingWithdrawal.set(null);
        this.withdrawing.set(false);
        this.loadHistory();
      },
      error: (err: HttpErrorResponse) => {
        this.errorMessage.set(this.describeError(err));
        // After a timeout, a server error or throttling we cannot tell whether the withdrawal happened,
        // so the confirmation stays open: pressing Confirm again resends the same key and is safe.
        const outcomeUnknown = err.status === 0 || err.status === 429 || err.status >= 500;
        if (!outcomeUnknown) {
          this.pendingWithdrawal.set(null);
        }
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
