using System.ComponentModel.DataAnnotations;

namespace WalletApp.Api.Contracts;

public sealed class WithdrawRequest
{
    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }
}
