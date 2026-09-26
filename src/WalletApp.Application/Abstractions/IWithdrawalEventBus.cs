using WalletApp.Domain;

namespace WalletApp.Application.Abstractions;

/// <summary>
/// Publishes withdrawal events to interested subscribers after they have already been durably
/// recorded (see <see cref="Domain.Abstractions.IWalletRepository.SaveWithdrawalAsync"/>).
/// This is a best-effort "live" notification channel; the persisted event log is the source of
/// truth, so a failure here must never fail the withdrawal request itself.
/// </summary>
public interface IWithdrawalEventBus
{
    Task PublishAsync(WithdrawalCompleted withdrawalEvent, CancellationToken cancellationToken = default);
}
