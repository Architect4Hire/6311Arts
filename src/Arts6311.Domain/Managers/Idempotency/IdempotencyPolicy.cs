namespace Arts6311.Domain.Managers.Idempotency;

/// <summary>Idempotency limits (api-contract.md: retryable commands take an <c>Idempotency-Key</c>).</summary>
/// <remarks>The error codes are in <c>ErrorCodes</c>; the API maps them to HTTP in 1.7 (key reused → 422).</remarks>
public static class IdempotencyPolicy
{
    /// <summary>How long a completed key replays its result before the same key may run fresh.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public const int KeyMaxLength = 255;
    public const int OperationMaxLength = 100;

    /// <summary>A prefix (<c>user:</c>, <c>cart:</c>) plus an ASP.NET Core Identity user id (450) or another handle.</summary>
    public const int SubjectMaxLength = 500;

    /// <summary>The header clients send.</summary>
    public const string KeyHeader = "Idempotency-Key";

    /// <summary>The header set on a replayed response.</summary>
    public const string ReplayedHeader = "Idempotent-Replayed";

    /// <summary>How often the Worker sweeps expired records.</summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    /// <summary>Rows one sweep DELETE removes; a sweep repeats until a batch comes back short.</summary>
    public const int SweepBatchSize = 500;
}

/// <summary>Server secret keying the request fingerprint HMAC (section <c>Idempotency</c>).</summary>
public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    /// <summary>
    /// Base64, at least 32 bytes. A secret: the AppHost's generated <c>idempotency-fingerprint-key</c> parameter
    /// locally, a Key Vault reference when published. Never in appsettings.
    /// </summary>
    public string FingerprintKey
    {
        get => fingerprintKey;
        set => (fingerprintKey, keyBytes) = (value, null);
    }

    private string fingerprintKey = string.Empty;
    private byte[]? keyBytes;

    /// <summary>The decoded key, decoded once: the options instance is a singleton, built and validated at startup.</summary>
    internal byte[] KeyBytes => keyBytes ??= Convert.FromBase64String(fingerprintKey);

    public static bool IsValidKey(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(value).Length >= 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
