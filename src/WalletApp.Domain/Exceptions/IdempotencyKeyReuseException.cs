namespace WalletApp.Domain.Exceptions;

/// <summary>Raised when an idempotency key that already completed a withdrawal is reused for a different request.</summary>
public sealed class IdempotencyKeyReuseException : Exception
{
    public string IdempotencyKey { get; }

    public IdempotencyKeyReuseException(string idempotencyKey)
        : base($"The idempotency key '{idempotencyKey}' was already used for a different withdrawal.")
    {
        IdempotencyKey = idempotencyKey;
    }
}
