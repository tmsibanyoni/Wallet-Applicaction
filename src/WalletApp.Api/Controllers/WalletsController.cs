using Microsoft.AspNetCore.Mvc;
using WalletApp.Api.Contracts;
using WalletApp.Application.Wallets;

namespace WalletApp.Api.Controllers;

[ApiController]
[Route("api/wallets")]
[Produces("application/json")]
public sealed class WalletsController : ControllerBase
{
    private readonly IWalletService _walletService;

    public WalletsController(IWalletService walletService)
    {
        _walletService = walletService;
    }

    /// <summary>Gets the current balance of a wallet.</summary>
    /// <param name="walletId">The wallet's unique id.</param>
    [HttpGet("{walletId:int}/balance")]
    [ProducesResponseType(typeof(BalanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BalanceResponse>> GetBalance(int walletId, CancellationToken cancellationToken)
    {
        var balance = await _walletService.GetBalanceAsync(walletId, cancellationToken);
        return Ok(balance);
    }

    /// <summary>
    /// Withdraws funds from a wallet. Succeeds only when the wallet has sufficient funds; the
    /// balance is updated and a withdrawal event is emitted atomically with the withdrawal.
    /// </summary>
    /// <param name="walletId">The wallet's unique id.</param>
    /// <param name="request">The amount to withdraw.</param>
    [HttpPost("{walletId:int}/withdrawals")]
    [ProducesResponseType(typeof(WithdrawResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<WithdrawResult>> Withdraw(
        int walletId, [FromBody] WithdrawRequest request, CancellationToken cancellationToken)
    {
        var result = await _walletService.WithdrawAsync(
            new WithdrawCommand(walletId, request.Amount, request.Currency), cancellationToken);
        return Ok(result);
    }
}
