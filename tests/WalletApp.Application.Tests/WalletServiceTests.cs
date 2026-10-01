using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WalletApp.Application.Wallets;
using WalletApp.Domain;
using WalletApp.Domain.Abstractions;
using WalletApp.Domain.Exceptions;
using Xunit;

namespace WalletApp.Application.Tests;

public class WalletServiceTests
{
    private readonly Mock<IWalletRepository> _repository = new();
    private readonly WalletService _sut;

    public WalletServiceTests()
    {
        var options = Options.Create(new WalletServiceOptions
        {
            MaxConcurrencyRetries = 3,
            MaxIdempotencyKeyLength = 100,
            HistoryDefaultPageSize = 20,
            HistoryMaxPageSize = 100,
        });
        _sut = new WalletService(_repository.Object, options, NullLogger<WalletService>.Instance);
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
    public async Task WithdrawAsync_Success_SavesWithEventAndReturnsResult()
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
    }

    [Fact]
    public async Task WithdrawAsync_InsufficientFunds_ThrowsAndNeverSaves()
    {
        var wallet = NewWallet(10m);
        _repository.Setup(r => r.GetByIdAsync(wallet.Id, It.IsAny<CancellationToken>())).ReturnsAsync(wallet);

        await Assert.ThrowsAsync<InsufficientFundsException>(() => _sut.WithdrawAsync(new WithdrawCommand(wallet.Id, 20m)));

        _repository.Verify(r => r.SaveWithdrawalAsync(It.IsAny<Wallet>(), It.IsAny<WithdrawalCompleted>(), It.IsAny<CancellationToken>()), Times.Never);
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

    }

    [Fact]
    public async Task GetWithdrawalsAsync_ReturnsSummariesInRepositoryOrder()
    {
        var wallet = NewWallet();
        var newer = StoredWithdrawal(wallet.Id, 10m, 90m, "a");
        var older = StoredWithdrawal(wallet.Id, 20m, 100m, "b");
        _repository.Setup(r => r.GetByIdAsync(wallet.Id, It.IsAny<CancellationToken>())).ReturnsAsync(wallet);
        _repository.Setup(r => r.GetWithdrawalsAsync(wallet.Id, 20, It.IsAny<CancellationToken>())).ReturnsAsync([newer, older]);

        var history = await _sut.GetWithdrawalsAsync(wallet.Id);

        Assert.Equal([newer.EventId, older.EventId], history.Select(h => h.WithdrawalId));
        Assert.Equal(10m, history[0].Amount);
        Assert.Equal(90m, history[0].BalanceAfter);
    }

    [Fact]
    public async Task GetWithdrawalsAsync_MissingWallet_Throws()
    {
        _repository.Setup(r => r.GetByIdAsync(404, It.IsAny<CancellationToken>())).ReturnsAsync((Wallet?)null);

        await Assert.ThrowsAsync<WalletNotFoundException>(() => _sut.GetWithdrawalsAsync(404));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task GetWithdrawalsAsync_LimitOutsideRange_Throws(int limit)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _sut.GetWithdrawalsAsync(1, limit));
    }

    private static WithdrawalCompleted StoredWithdrawal(int walletId, decimal amount, decimal balanceAfter, string key)
        => new(Guid.NewGuid(), walletId, amount, balanceAfter, "USD", DateTimeOffset.UtcNow, key);

    [Fact]
    public async Task WithdrawAsync_KeyAlreadyUsedWithSameRequest_ReplaysStoredResultWithoutSaving()
    {
        var wallet = NewWallet(60m);
        var stored = StoredWithdrawal(wallet.Id, 40m, 60m, "key-1");
        _repository.Setup(r => r.GetByIdAsync(wallet.Id, It.IsAny<CancellationToken>())).ReturnsAsync(wallet);
        _repository.Setup(r => r.FindWithdrawalByKeyAsync(wallet.Id, "key-1", It.IsAny<CancellationToken>())).ReturnsAsync(stored);

        var result = await _sut.WithdrawAsync(new WithdrawCommand(wallet.Id, 40m, "USD", "key-1"));

        Assert.True(result.Replayed);
        Assert.Equal(stored.EventId, result.WithdrawalId);
        Assert.Equal(60m, result.BalanceAfter);
        Assert.Equal(60m, wallet.Balance);
        _repository.Verify(r => r.SaveWithdrawalAsync(It.IsAny<Wallet>(), It.IsAny<WithdrawalCompleted>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithdrawAsync_KeyAlreadyUsedWithDifferentAmount_ThrowsReuseException()
    {
        var wallet = NewWallet(60m);
        _repository.Setup(r => r.GetByIdAsync(wallet.Id, It.IsAny<CancellationToken>())).ReturnsAsync(wallet);
        _repository.Setup(r => r.FindWithdrawalByKeyAsync(wallet.Id, "key-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoredWithdrawal(wallet.Id, 40m, 60m, "key-1"));

        await Assert.ThrowsAsync<IdempotencyKeyReuseException>(
            () => _sut.WithdrawAsync(new WithdrawCommand(wallet.Id, 10m, null, "key-1")));

        _repository.Verify(r => r.SaveWithdrawalAsync(It.IsAny<Wallet>(), It.IsAny<WithdrawalCompleted>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithdrawAsync_NewKey_StoresTheKeyOnTheEvent()
    {
        var wallet = NewWallet(100m);
        _repository.Setup(r => r.GetByIdAsync(wallet.Id, It.IsAny<CancellationToken>())).ReturnsAsync(wallet);

        var result = await _sut.WithdrawAsync(new WithdrawCommand(wallet.Id, 25m, null, "  key-2  "));

        Assert.False(result.Replayed);
        _repository.Verify(r => r.SaveWithdrawalAsync(
            wallet,
            It.Is<WithdrawalCompleted>(e => e.IdempotencyKey == "key-2"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WithdrawAsync_LostRaceOnKey_RetriesAndReplaysTheWinnersResult()
    {
        var wallet = NewWallet(100m);
        var winner = StoredWithdrawal(wallet.Id, 25m, 75m, "key-3");
        _repository.Setup(r => r.GetByIdAsync(wallet.Id, It.IsAny<CancellationToken>())).ReturnsAsync(wallet);
        _repository.SetupSequence(r => r.FindWithdrawalByKeyAsync(wallet.Id, "key-3", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WithdrawalCompleted?)null)
            .ReturnsAsync(winner);
        _repository.Setup(r => r.SaveWithdrawalAsync(It.IsAny<Wallet>(), It.IsAny<WithdrawalCompleted>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateIdempotencyKeyException(wallet.Id, "key-3", new Exception("duplicate")));

        var result = await _sut.WithdrawAsync(new WithdrawCommand(wallet.Id, 25m, null, "key-3"));

        Assert.True(result.Replayed);
        Assert.Equal(winner.EventId, result.WithdrawalId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WithdrawAsync_BlankKey_IsRejected(string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.WithdrawAsync(new WithdrawCommand(1, 10m, null, key)));
    }

    [Fact]
    public async Task WithdrawAsync_KeyLongerThanLimit_IsRejected()
    {
        var key = new string('k', 101);

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.WithdrawAsync(new WithdrawCommand(1, 10m, null, key)));
    }
}
