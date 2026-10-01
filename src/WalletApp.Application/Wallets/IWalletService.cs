namespace WalletApp.Application.Wallets;

public interface IWalletService
{
    Task<BalanceResponse> GetBalanceAsync(int walletId, CancellationToken cancellationToken = default);

    Task<WithdrawResult> WithdrawAsync(WithdrawCommand command, CancellationToken cancellationToken = default);
}
