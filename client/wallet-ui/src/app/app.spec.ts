import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../environments/environment';
import { App } from './app';

describe('App', () => {
  let httpMock: HttpTestingController;

  const balanceUrl = `${environment.apiBaseUrl}/wallets/${environment.defaultWalletId}/balance`;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  // Loading the wallet also asks for its recent withdrawals; tests that don't care just let it come back empty.
  afterEach(() => {
    httpMock
      .match((req) => req.method === 'GET' && req.url.endsWith('/withdrawals'))
      .forEach((req) => req.flush([]));
    httpMock.verify();
  });

  it('loads the wallet balance on init', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    httpMock.expectOne(balanceUrl).flush({ walletId: Number(environment.defaultWalletId), balance: 1000, currency: 'USD' });

    expect(fixture.componentInstance.balance()).toBe(1000);
    expect(fixture.componentInstance.currency()).toBe('USD');
  });

  it('shows rand amounts with the R symbol instead of the ZAR code', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpMock.expectOne(balanceUrl).flush({ walletId: Number(environment.defaultWalletId), balance: 1500, currency: 'ZAR' });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).querySelector('.balance-value')?.textContent ?? '';
    expect(text).toContain('R');
    expect(text).toContain('500');
    expect(text).not.toContain('ZAR');
  });

  it('renders the wallet heading', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpMock.expectOne(balanceUrl).flush({ walletId: Number(environment.defaultWalletId), balance: 1000, currency: 'USD' });

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('Wallet');
  });

  it('shows an error message when the balance request fails', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    httpMock.expectOne(balanceUrl).flush(
      { title: 'Wallet not found', status: 404, detail: "Wallet 'x' was not found." },
      { status: 404, statusText: 'Not Found' },
    );

    expect(fixture.componentInstance.errorMessage()).toContain('not found');
  });

  it('updates the balance after a successful withdrawal', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpMock.expectOne(balanceUrl).flush({ walletId: Number(environment.defaultWalletId), balance: 1000, currency: 'USD' });

    fixture.componentInstance.withdrawAmount = 100;
    fixture.componentInstance.requestWithdrawal();
    fixture.componentInstance.confirmWithdrawal();

    const withdrawReq = httpMock.expectOne(`${environment.apiBaseUrl}/wallets/${environment.defaultWalletId}/withdrawals`);
    expect(withdrawReq.request.body).toEqual({ amount: 100 });

    withdrawReq.flush({
      withdrawalId: 'w1',
      walletId: Number(environment.defaultWalletId),
      amount: 100,
      balanceAfter: 900,
      currency: 'USD',
      occurredAtUtc: new Date().toISOString(),
    });

    expect(fixture.componentInstance.balance()).toBe(900);
    expect(fixture.componentInstance.successMessage()).toContain('900');
  });

  it('picks up a balance changed elsewhere when refreshed, without flashing the loading state', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpMock.expectOne(balanceUrl).flush({ walletId: Number(environment.defaultWalletId), balance: 1000, currency: 'ZAR' });

    fixture.componentInstance.refreshBalance();
    expect(fixture.componentInstance.loadingBalance()).toBe(false);
    httpMock.expectOne(balanceUrl).flush({ walletId: Number(environment.defaultWalletId), balance: 400, currency: 'ZAR' });

    expect(fixture.componentInstance.balance()).toBe(400);
  });

  it('keeps the last balance and shows no error when a background refresh fails', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpMock.expectOne(balanceUrl).flush({ walletId: Number(environment.defaultWalletId), balance: 1000, currency: 'ZAR' });

    fixture.componentInstance.refreshBalance();
    httpMock.expectOne(balanceUrl).flush(null, { status: 500, statusText: 'Server Error' });

    expect(fixture.componentInstance.balance()).toBe(1000);
    expect(fixture.componentInstance.errorMessage()).toBeNull();
  });

  it('refreshes when the tab becomes visible again', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpMock.expectOne(balanceUrl).flush({ walletId: Number(environment.defaultWalletId), balance: 1000, currency: 'ZAR' });

    document.dispatchEvent(new Event('visibilitychange'));

    httpMock.expectOne(balanceUrl).flush({ walletId: Number(environment.defaultWalletId), balance: 250, currency: 'ZAR' });
    expect(fixture.componentInstance.balance()).toBe(250);
  });

  it('does not refresh over an in-flight withdrawal', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpMock.expectOne(balanceUrl).flush({ walletId: Number(environment.defaultWalletId), balance: 1000, currency: 'ZAR' });

    fixture.componentInstance.withdrawAmount = 100;
    fixture.componentInstance.requestWithdrawal();
    fixture.componentInstance.confirmWithdrawal();
    fixture.componentInstance.refreshBalance();

    httpMock.expectNone(balanceUrl);
    httpMock.expectOne(`${environment.apiBaseUrl}/wallets/${environment.defaultWalletId}/withdrawals`).flush(
      { withdrawalId: 'w1', walletId: Number(environment.defaultWalletId), amount: 100, balanceAfter: 900, currency: 'ZAR', occurredAtUtc: new Date().toISOString() });
  });

  it('shows a plain message, not the API address, when the service cannot be reached', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    httpMock.expectOne(balanceUrl).error(new ProgressEvent('error'));

    const message = fixture.componentInstance.errorMessage() ?? '';
    expect(message).toContain('cannot reach the wallet service');
    expect(message).not.toContain('localhost');
    expect(consoleError).toHaveBeenCalled();
    consoleError.mockRestore();
  });

  it('hides server error detail from the user, quotes the reference and logs the detail', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    httpMock.expectOne(balanceUrl).flush(
      { title: 'An unexpected error occurred', status: 500, detail: 'SqlException: login failed', traceId: 'abc-123' },
      { status: 500, statusText: 'Server Error' },
    );

    const message = fixture.componentInstance.errorMessage() ?? '';
    expect(message).toContain('Something went wrong on our side');
    expect(message).toContain('abc-123');
    expect(message).not.toContain('SqlException');
    expect(consoleError).toHaveBeenCalledWith('Wallet API request failed', expect.objectContaining({ status: 500, traceId: 'abc-123' }));
    consoleError.mockRestore();
  });

  describe('withdrawal history', () => {
    const historyUrl = `${environment.apiBaseUrl}/wallets/${environment.defaultWalletId}/withdrawals?limit=${environment.historyPageSize}`;
    const entry = { withdrawalId: 'w1', amount: 100, balanceAfter: 900, currency: 'ZAR', occurredAtUtc: '2026-10-01T10:00:00Z' };

    function loadedFixture() {
      const fixture = TestBed.createComponent(App);
      fixture.detectChanges();
      httpMock.expectOne(balanceUrl).flush({ walletId: Number(environment.defaultWalletId), balance: 1000, currency: 'ZAR' });
      return fixture;
    }

    function show(fixture: ReturnType<typeof loadedFixture>) {
      (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('#toggle-history')!.click();
      fixture.detectChanges();
    }

    it('keeps the list hidden until Show is clicked, then hides it again', () => {
      const fixture = loadedFixture();
      httpMock.expectOne(historyUrl).flush([entry]);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;

      expect(root.querySelector('#history-body')).toBeNull();
      expect(root.querySelector('#toggle-history')?.textContent?.trim()).toBe('Show');

      show(fixture);
      expect(root.querySelector('#history-body')).not.toBeNull();
      expect(root.querySelector('#toggle-history')?.textContent?.trim()).toBe('Hide');

      show(fixture);
      expect(root.querySelector('#history-body')).toBeNull();
    });

    it('loads the recent withdrawals along with the balance and lists them', () => {
      const fixture = loadedFixture();

      httpMock.expectOne(historyUrl).flush([entry]);
      fixture.detectChanges();
      show(fixture);

      expect(fixture.componentInstance.history()).toEqual([entry]);
      expect((fixture.nativeElement as HTMLElement).querySelector('.history')?.textContent).toContain('R');
    });

    it('says there are no withdrawals yet when the list is empty', () => {
      const fixture = loadedFixture();

      httpMock.expectOne(historyUrl).flush([]);
      fixture.detectChanges();
      show(fixture);

      expect((fixture.nativeElement as HTMLElement).querySelector('.history')?.textContent).toContain('No withdrawals yet');
    });

    it('reloads the list after a withdrawal', () => {
      const fixture = loadedFixture();
      httpMock.expectOne(historyUrl).flush([]);

      fixture.componentInstance.withdrawAmount = 100;
      fixture.componentInstance.requestWithdrawal();
      fixture.componentInstance.confirmWithdrawal();
      httpMock.expectOne(`${environment.apiBaseUrl}/wallets/${environment.defaultWalletId}/withdrawals`).flush({
        withdrawalId: 'w1', walletId: Number(environment.defaultWalletId), amount: 100,
        balanceAfter: 900, currency: 'ZAR', occurredAtUtc: entry.occurredAtUtc,
      });

      httpMock.expectOne(historyUrl).flush([entry]);
      expect(fixture.componentInstance.history()).toEqual([entry]);
    });

    it('keeps the old list and shows no error when the history request fails', () => {
      const fixture = loadedFixture();
      httpMock.expectOne(historyUrl).flush([entry]);

      fixture.componentInstance.loadHistory();
      httpMock.expectOne(historyUrl).flush({}, { status: 500, statusText: 'Server Error' });

      expect(fixture.componentInstance.history()).toEqual([entry]);
      expect(fixture.componentInstance.errorMessage()).toBeNull();
    });
  });

  describe('withdrawal confirmation', () => {
    const withdrawalsUrl = `${environment.apiBaseUrl}/wallets/${environment.defaultWalletId}/withdrawals`;

    function loadedFixture(balance = 1000) {
      const fixture = TestBed.createComponent(App);
      fixture.detectChanges();
      httpMock.expectOne(balanceUrl).flush({ walletId: Number(environment.defaultWalletId), balance, currency: 'ZAR' });
      fixture.detectChanges();
      return fixture;
    }

    it('asks for confirmation and sends nothing until the user confirms', () => {
      const fixture = loadedFixture();

      fixture.componentInstance.withdrawAmount = 100;
      fixture.componentInstance.requestWithdrawal();
      fixture.detectChanges();

      httpMock.expectNone(withdrawalsUrl);
      const panel = (fixture.nativeElement as HTMLElement).querySelector('.confirm')?.textContent ?? '';
      expect(panel).toContain('Confirm withdrawal');
      expect(panel).toContain('100');
      expect(panel).toContain('900');
    });

    it('sends the withdrawal only after Confirm is clicked', () => {
      const fixture = loadedFixture();
      fixture.componentInstance.withdrawAmount = 100;
      fixture.componentInstance.requestWithdrawal();
      fixture.detectChanges();

      (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('#confirm-withdrawal')!.click();

      httpMock.expectOne(withdrawalsUrl).flush({
        withdrawalId: 'w1', walletId: Number(environment.defaultWalletId), amount: 100,
        balanceAfter: 900, currency: 'ZAR', occurredAtUtc: new Date().toISOString(),
      });
      expect(fixture.componentInstance.pendingWithdrawal()).toBeNull();
      expect(fixture.componentInstance.balance()).toBe(900);
    });

    it('cancels without calling the API', () => {
      const fixture = loadedFixture();
      fixture.componentInstance.withdrawAmount = 100;
      fixture.componentInstance.requestWithdrawal();
      fixture.detectChanges();

      (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('#cancel-withdrawal')!.click();
      fixture.detectChanges();

      httpMock.expectNone(withdrawalsUrl);
      expect(fixture.componentInstance.pendingWithdrawal()).toBeNull();
      expect((fixture.nativeElement as HTMLElement).querySelector('.confirm')).toBeNull();
      expect(fixture.componentInstance.balance()).toBe(1000);
    });

    it('cancels when Escape is pressed', () => {
      const fixture = loadedFixture();
      fixture.componentInstance.withdrawAmount = 100;
      fixture.componentInstance.requestWithdrawal();

      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));

      expect(fixture.componentInstance.pendingWithdrawal()).toBeNull();
    });

    it('refuses an amount above the balance before asking for confirmation', () => {
      const fixture = loadedFixture(50);

      fixture.componentInstance.withdrawAmount = 75;
      fixture.componentInstance.requestWithdrawal();

      expect(fixture.componentInstance.pendingWithdrawal()).toBeNull();
      expect(fixture.componentInstance.errorMessage()).toContain('up to');
      httpMock.expectNone(withdrawalsUrl);
    });

    it('refuses an amount with more than two decimal places', () => {
      const fixture = loadedFixture();

      fixture.componentInstance.withdrawAmount = 10.005;
      fixture.componentInstance.requestWithdrawal();

      expect(fixture.componentInstance.pendingWithdrawal()).toBeNull();
      expect(fixture.componentInstance.errorMessage()).toContain('two decimal places');
    });

    it('sends an Idempotency-Key header with the withdrawal', () => {
      const fixture = loadedFixture();
      fixture.componentInstance.withdrawAmount = 100;
      fixture.componentInstance.requestWithdrawal();
      fixture.componentInstance.confirmWithdrawal();

      const req = httpMock.expectOne(withdrawalsUrl);
      expect(req.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
      expect(req.request.body).toEqual({ amount: 100 });
    });

    it('keeps the confirmation open after a network failure and retries with the same key', () => {
      const fixture = loadedFixture();
      fixture.componentInstance.withdrawAmount = 100;
      fixture.componentInstance.requestWithdrawal();
      fixture.componentInstance.confirmWithdrawal();

      const first = httpMock.expectOne(withdrawalsUrl);
      const key = first.request.headers.get('Idempotency-Key');
      first.error(new ProgressEvent('error'), { status: 0 });

      expect(fixture.componentInstance.pendingWithdrawal()).not.toBeNull();

      fixture.componentInstance.confirmWithdrawal();
      const second = httpMock.expectOne(withdrawalsUrl);
      expect(second.request.headers.get('Idempotency-Key')).toBe(key);
    });

    it('closes the confirmation after a rejected withdrawal so the next attempt gets a new key', () => {
      const fixture = loadedFixture();
      fixture.componentInstance.withdrawAmount = 100;
      fixture.componentInstance.requestWithdrawal();
      fixture.componentInstance.confirmWithdrawal();

      httpMock.expectOne(withdrawalsUrl).flush(
        { title: 'Insufficient funds', detail: 'The wallet does not have enough money.' },
        { status: 422, statusText: 'Unprocessable Entity' },
      );

      expect(fixture.componentInstance.pendingWithdrawal()).toBeNull();
    });

    it('says so when the server replays an earlier withdrawal instead of taking the money twice', () => {
      const fixture = loadedFixture();
      fixture.componentInstance.withdrawAmount = 100;
      fixture.componentInstance.requestWithdrawal();
      fixture.componentInstance.confirmWithdrawal();

      httpMock.expectOne(withdrawalsUrl).flush({
        withdrawalId: 'w1', walletId: Number(environment.defaultWalletId), amount: 100,
        balanceAfter: 900, currency: 'ZAR', occurredAtUtc: new Date().toISOString(), replayed: true,
      });

      expect(fixture.componentInstance.successMessage()).toContain('already gone through');
    });

    it('will not confirm if the balance dropped below the amount while the panel was open', () => {
      const fixture = loadedFixture(100);
      fixture.componentInstance.withdrawAmount = 80;
      fixture.componentInstance.requestWithdrawal();

      fixture.componentInstance.refreshBalance();
      httpMock.expectOne(balanceUrl).flush({ walletId: Number(environment.defaultWalletId), balance: 30, currency: 'ZAR' });
      fixture.detectChanges();

      expect(fixture.componentInstance.canConfirmWithdrawal()).toBe(false);
      fixture.componentInstance.confirmWithdrawal();
      httpMock.expectNone(withdrawalsUrl);
    });
  });
});
