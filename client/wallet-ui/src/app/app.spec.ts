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
});
