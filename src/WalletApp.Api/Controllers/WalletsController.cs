using Microsoft.AspNetCore.Mvc;
using WalletApp.Api.Contracts;
using WalletApp.Application.Wallets;

namespace WalletApp.Api.Controllers;

[ApiController]
[Route("api/wallets")]
[Produces("application/json")]
public sealed class WalletsController : ControllerBase
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string IdempotentReplayedHeader = "Idempotent-Replayed";

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
    /// <remarks>
    /// Send an <c>Idempotency-Key</c> header to make retries safe: repeating the same request with the
    /// same key returns the original result (with an <c>Idempotent-Replayed: true</c> header) and does
    /// not withdraw again. Reusing a key with a different amount or currency is rejected with 422.
    /// </remarks>
    /// <param name="walletId">The wallet's unique id.</param>
    /// <param name="request">The amount to withdraw.</param>
    /// <param name="idempotencyKey">Optional client-generated key (up to 100 characters), unique per withdrawal.</param>
    [HttpPost("{walletId:int}/withdrawals")]
    [ProducesResponseType(typeof(WithdrawResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<WithdrawResult>> Withdraw(
        int walletId,
        [FromBody] WithdrawRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await _walletService.WithdrawAsync(
            new WithdrawCommand(walletId, request.Amount, request.Currency, idempotencyKey), cancellationToken);

        if (result.Replayed)
        {
            Response.Headers[IdempotentReplayedHeader] = "true";
        }

        return Ok(result);
    }
}
