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

  afterEach(() => httpMock.verify());

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
    fixture.componentInstance.withdraw();

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
    fixture.componentInstance.withdraw();
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
});
