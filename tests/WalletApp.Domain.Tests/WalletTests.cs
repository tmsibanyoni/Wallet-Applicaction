using WalletApp.Domain;
using WalletApp.Domain.Exceptions;
using Xunit;

namespace WalletApp.Domain.Tests;

public class WalletTests
{
    private static Wallet CreateWallet(decimal balance = 100m, int id = 1) =>
        new(id, "Test Owner", balance, "USD");

    [Fact]
    public void Constructor_WithValidArguments_SetsInitialState()
    {
        var wallet = new Wallet(42, "Ada Lovelace", 500m, "USD");

        Assert.Equal(42, wallet.Id);
        Assert.Equal("Ada Lovelace", wallet.OwnerName);
        Assert.Equal(500m, wallet.Balance);
        Assert.Equal("USD", wallet.Currency);
        Assert.Equal(0, wallet.Version);
    }

    [Fact]
    public void Constructor_WithNegativeInitialBalance_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Wallet(1, "Owner", -1m, "USD"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithNonPositiveId_Throws(int id)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Wallet(id, "Owner", 100m, "USD"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithoutOwnerName_Throws(string? ownerName)
    {
        Assert.Throws<ArgumentException>(() => new Wallet(1, ownerName!, 100m, "USD"));
    }

    [Fact]
    public void Withdraw_WithSufficientFunds_ReducesBalanceAndReturnsMatchingEvent()
    {
        var wallet = CreateWallet(100m);

        var result = wallet.Withdraw(40m);

        Assert.Equal(60m, wallet.Balance);
        Assert.Equal(wallet.Id, result.WalletId);
        Assert.Equal(40m, result.Amount);
        Assert.Equal(60m, result.BalanceAfter);
        Assert.Equal(wallet.Currency, result.Currency);
        Assert.NotEqual(Guid.Empty, result.EventId);
    }

    [Fact]
    public void Withdraw_ForExactBalance_SucceedsAndLeavesZeroBalance()
    {
        var wallet = CreateWallet(100m);

        var result = wallet.Withdraw(100m);

        Assert.Equal(0m, wallet.Balance);
        Assert.Equal(0m, result.BalanceAfter);
    }

    [Fact]
    public void Withdraw_MoreThanBalance_ThrowsAndLeavesBalanceUnchanged()
    {
        var wallet = CreateWallet(100m);

        var ex = Assert.Throws<InsufficientFundsException>(() => wallet.Withdraw(100.01m));

        Assert.Equal(wallet.Id, ex.WalletId);
        Assert.Equal(100m, ex.CurrentBalance);
        Assert.Equal(100.01m, ex.RequestedAmount);
        Assert.Equal(100m, wallet.Balance); // never goes negative / never mutated on failure
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Withdraw_WithNonPositiveAmount_ThrowsAndLeavesBalanceUnchanged(decimal amount)
    {
        var wallet = CreateWallet(100m);

        Assert.Throws<ArgumentOutOfRangeException>(() => wallet.Withdraw(amount));

        Assert.Equal(100m, wallet.Balance);
    }

    [Fact]
    public void Withdraw_OnSuccess_IncrementsVersionForOptimisticConcurrency()
    {
        var wallet = CreateWallet(100m);

        wallet.Withdraw(10m);
        wallet.Withdraw(10m);

        Assert.Equal(2, wallet.Version);
    }

    [Fact]
    public void Withdraw_OnFailure_DoesNotIncrementVersion()
    {
        var wallet = CreateWallet(100m);

        Assert.Throws<InsufficientFundsException>(() => wallet.Withdraw(1000m));

        Assert.Equal(0, wallet.Version);
    }
}
