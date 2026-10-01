using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WalletApp.Application.Abstractions;
using WalletApp.Application.Wallets;
using WalletApp.Domain;
using WalletApp.Domain.Abstractions;
using WalletApp.Domain.Exceptions;
using Xunit;

namespace WalletApp.Application.Tests;

public class WalletServiceTests
{
    private readonly Mock<IWalletRepository> _repository = new();
    private readonly Mock<IWithdrawalEventBus> _eventBus = new();
    private readonly WalletService _sut;

    public WalletServiceTests()
    {
        var options = Options.Create(new WalletServiceOptions { MaxConcurrencyRetries = 3 });
        _sut = new WalletService(_repository.Object, _eventBus.Object, options, NullLogger<WalletService>.Instance);
    }

    private static Wallet NewWallet(decimal balance = 100m, int id = 1) => new(id, "Owner", balance, "USD");

    [Fact]
    public async Task GetBalanceAsync_ExistingWallet_ReturnsBalance()
    {
        var wallet = NewWallet(250m);
        _repository.Setup(r => r.GetByIdAsync(wallet.Id, It.IsAny<CancellationToken>())).ReturnsAsync(wallet);

        var result = await _sut.GetBalanceAsync(wallet.Id);

        Assert.Equal(wallet.Id, result.WalletId);
        Assert.Equal(250m, result.Balance);
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public async Task GetBalanceAsync_MissingWallet_ThrowsWalletNotFoundException()
    {
        const int walletId = 404;
        _repository.Setup(r => r.GetByIdAsync(walletId, It.IsAny<CancellationToken>())).ReturnsAsync((Wallet?)null);

        await Assert.ThrowsAsync<WalletNotFoundException>(() => _sut.GetBalanceAsync(walletId));
    }

    [Fact]
    public async Task WithdrawAsync_Success_SavesAndPublishesEventAndReturnsResult()
    {
        var wallet = NewWallet(100m);
        _repository.Setup(r => r.GetByIdAsync(wallet.Id, It.IsAny<CancellationToken>())).ReturnsAsync(wallet);

        var result = await _sut.WithdrawAsync(new WithdrawCommand(wallet.Id, 40m));

        Assert.Equal(60m, result.BalanceAfter);
        Assert.Equal(40m, result.Amount);
        Assert.Equal(wallet.Id, result.WalletId);

        _repository.Verify(r => r.SaveWithdrawalAsync(
            wallet,
            It.Is<WithdrawalCompleted>(e => e.Amount == 40m && e.BalanceAfter == 60m),
            It.IsAny<CancellationToken>()), Times.Once);

        _eventBus.Verify(b => b.PublishAsync(
            It.Is<WithdrawalCompleted>(e => e.WalletId == wallet.Id && e.Amount == 40m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WithdrawAsync_CurrencyDifferentFromWallet_ThrowsAndNeverSaves()
    {
        var wallet = NewWallet(100m);
        _repository.Setup(r => r.GetByIdAsync(wallet.Id, It.IsAny<CancellationToken>())).ReturnsAsync(wallet);

        await Assert.ThrowsAsync<CurrencyMismatchException>(() => _sut.WithdrawAsync(new WithdrawCommand(wallet.Id, 10m, "ZAR")));

        _repository.Verify(r => r.SaveWithdrawalAsync(It.IsAny<Wallet>(), It.IsAny<WithdrawalCompleted>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(100m, wallet.Balance);
    }

    [Fact]
    public async Task WithdrawAsync_MatchingCurrencyInAnyCase_Succeeds()
    {
        var wallet = NewWallet(100m);
        _repository.Setup(r => r.GetByIdAsync(wallet.Id, It.IsAny<CancellationToken>())).ReturnsAsync(wallet);

        var result = await _sut.WithdrawAsync(new WithdrawCommand(wallet.Id, 10m, "usd"));

        Assert.Equal(90m, result.BalanceAfter);
    }

    [Fact]
    public async Task WithdrawAsync_MissingWallet_ThrowsAndNeverSaves()
    {
        const int walletId = 404;
        _repository.Setup(r => r.GetByIdAsync(walletId, It.IsAny<CancellationToken>())).ReturnsAsync((Wallet?)null);

        await Assert.ThrowsAsync<WalletNotFoundException>(() => _sut.WithdrawAsync(new WithdrawCommand(walletId, 10m)));

        _repository.Verify(r => r.SaveWithdrawalAsync(It.IsAny<Wallet>(), It.IsAny<WithdrawalCompleted>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventBus.Verify(b => b.PublishAsync(It.IsAny<WithdrawalCompleted>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithdrawAsync_InsufficientFunds_ThrowsAndNeverSavesOrPublishes()
    {
        var wallet = NewWallet(10m);
        _repository.Setup(r => r.GetByIdAsync(wallet.Id, It.IsAny<CancellationToken>())).ReturnsAsync(wallet);

        await Assert.ThrowsAsync<InsufficientFundsException>(() => _sut.WithdrawAsync(new WithdrawCommand(wallet.Id, 20m)));

        _repository.Verify(r => r.SaveWithdrawalAsync(It.IsAny<Wallet>(), It.IsAny<WithdrawalCompleted>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventBus.Verify(b => b.PublishAsync(It.IsAny<WithdrawalCompleted>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(10m, wallet.Balance);
    }

    [Fact]
    public async Task WithdrawAsync_TransientConcurrencyConflict_RetriesWithFreshReadAndSucceeds()
    {
        // First attempt loads a wallet whose save then reports a concurrency conflict
        // (as if another request updated it first); the service should re-read and retry.
        var staleWallet = NewWallet(100m);
        var freshWallet = NewWallet(100m);

        _repository.SetupSequence(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(staleWallet)
            .ReturnsAsync(freshWallet);

        _repository.SetupSequence(r => r.SaveWithdrawalAsync(It.IsAny<Wallet>(), It.IsAny<WithdrawalCompleted>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException(staleWallet.Id, new Exception("conflict")))
            .Returns(Task.CompletedTask);

        var result = await _sut.WithdrawAsync(new WithdrawCommand(staleWallet.Id, 30m));

        Assert.Equal(70m, result.BalanceAfter);
        _repository.Verify(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _eventBus.Verify(b => b.PublishAsync(It.IsAny<WithdrawalCompleted>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WithdrawAsync_PersistentConcurrencyConflict_ThrowsAfterMaxRetries()
    {
        const int walletId = 1;

        // A real repository reloads the tracked wallet to the currently persisted state after a
        // failed save (see EfWalletRepository.SaveWithdrawalAsync), so a fresh GetByIdAsync
        // always reflects that reload rather than this attempt's discarded in-memory mutation.
        // A brand new instance per call models that "always reflects current DB state" contract
        // without needing a real DbContext.
        _repository.Setup(r => r.GetByIdAsync(walletId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new Wallet(walletId, "Owner", 100m, "USD"));
        _repository.Setup(r => r.SaveWithdrawalAsync(It.IsAny<Wallet>(), It.IsAny<WithdrawalCompleted>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException(walletId, new Exception("conflict")));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => _sut.WithdrawAsync(new WithdrawCommand(walletId, 10m)));

        _eventBus.Verify(b => b.PublishAsync(It.IsAny<WithdrawalCompleted>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithdrawAsync_EventBusThrows_StillReturnsSuccessfulResult()
    {
        // The durable save already succeeded; a live-bus failure must not fail the request.
        var wallet = NewWallet(100m);
        _repository.Setup(r => r.GetByIdAsync(wallet.Id, It.IsAny<CancellationToken>())).ReturnsAsync(wallet);
        _eventBus.Setup(b => b.PublishAsync(It.IsAny<WithdrawalCompleted>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bus unavailable"));

        var result = await _sut.WithdrawAsync(new WithdrawCommand(wallet.Id, 25m));

        Assert.Equal(75m, result.BalanceAfter);
    }
}
