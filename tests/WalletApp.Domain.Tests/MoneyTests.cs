using WalletApp.Domain;
using WalletApp.Domain.Exceptions;
using Xunit;

namespace WalletApp.Domain.Tests;

public class MoneyTests
{
    [Fact]
    public void Constructor_NormalisesCurrencyToUpperCase()
    {
        var money = new Money(10m, " zar ");

        Assert.Equal("ZAR", money.Currency);
        Assert.Equal(10m, money.Amount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDX")]
    [InlineData("12A")]
    public void Constructor_WithInvalidCurrency_Throws(string? currency)
    {
        Assert.Throws<ArgumentException>(() => new Money(1m, currency!));
    }

    [Fact]
    public void Constructor_WithNegativeAmount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(-0.01m, "ZAR"));
    }

    [Theory]
    [InlineData("10.005")]
    [InlineData("0.001")]
    public void Constructor_WithMoreThanTwoDecimalPlaces_Throws(string amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), "ZAR"));
    }

    [Fact]
    public void Subtract_SameCurrency_ReturnsDifference()
    {
        var result = new Money(100m, "ZAR").Subtract(new Money(30.50m, "ZAR"));

        Assert.Equal(new Money(69.50m, "ZAR"), result);
    }

    [Fact]
    public void Subtract_DifferentCurrency_Throws()
    {
        var ex = Assert.Throws<CurrencyMismatchException>(() => new Money(100m, "ZAR").Subtract(new Money(1m, "USD")));

        Assert.Equal("ZAR", ex.ExpectedCurrency);
        Assert.Equal("USD", ex.ActualCurrency);
    }

    [Fact]
    public void IsGreaterThan_DifferentCurrency_Throws()
    {
        Assert.Throws<CurrencyMismatchException>(() => new Money(100m, "ZAR").IsGreaterThan(new Money(1m, "USD")));
    }

    [Fact]
    public void Equality_IsByAmountAndCurrency()
    {
        Assert.Equal(new Money(5m, "ZAR"), new Money(5.00m, "zar"));
        Assert.NotEqual(new Money(5m, "ZAR"), new Money(5m, "USD"));
    }
}
