namespace WalletApp.Application.Wallets;

public interface IWalletService
{
    Task<BalanceResponse> GetBalanceAsync(int walletId, CancellationToken cancellationToken = default);

    Task<WithdrawResult> WithdrawAsync(WithdrawCommand command, CancellationToken cancellationToken = default);

    /// <summary>Lists the wallet's most recent withdrawals, newest first. A null limit uses the configured default.</summary>
    Task<IReadOnlyList<WithdrawalSummary>> GetWithdrawalsAsync(int walletId, int? limit = null, CancellationToken cancellationToken = default);
}
