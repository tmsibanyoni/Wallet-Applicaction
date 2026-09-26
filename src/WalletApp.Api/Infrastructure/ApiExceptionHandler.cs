using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WalletApp.Domain.Exceptions;

namespace WalletApp.Api.Infrastructure;

/// <summary>
/// Translates domain/application exceptions into RFC 7807 ProblemDetails responses, so
/// controllers stay free of try/catch and every error the API returns has a consistent shape.
/// </summary>
public sealed class ApiExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(ILogger<ApiExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            WalletNotFoundException => (StatusCodes.Status404NotFound, "Wallet not found"),
            InsufficientFundsException => (StatusCodes.Status409Conflict, "Insufficient funds"),
            ArgumentOutOfRangeException or ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            ConcurrencyConflictException => (StatusCodes.Status503ServiceUnavailable, "Wallet is busy, please retry"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred"),
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception processing {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            _logger.LogInformation(exception, "Request {Method} {Path} failed with {StatusCode}", httpContext.Request.Method, httpContext.Request.Path, statusCode);
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = exception.Message,
            Instance = httpContext.Request.Path,
        }, cancellationToken);

        return true;
    }
}
