namespace WalletApp.Application.Wallets;

public interface IWalletService
{
    Task<BalanceResponse> GetBalanceAsync(Guid walletId, CancellationToken cancellationToken = default);

    Task<WithdrawResult> WithdrawAsync(Guid walletId, decimal amount, CancellationToken cancellationToken = default);
}
