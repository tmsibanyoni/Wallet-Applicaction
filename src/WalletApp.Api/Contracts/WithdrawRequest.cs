using System.ComponentModel.DataAnnotations;

namespace WalletApp.Api.Contracts;

public sealed class WithdrawRequest
{
    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    /// <summary>Optional ISO 4217 code. If supplied it must match the wallet's currency.</summary>
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Currency must be a three-letter ISO 4217 code.")]
    public string? Currency { get; set; }
}
