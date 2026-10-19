namespace Arts6311.AppHost;

/// <summary>
/// Which Square the run-mode system talks to (ADR 0007). AppHost configuration, never a dashboard prompt:
/// it decides which resources exist, so it has to be known before the model is built.
/// </summary>
internal enum SquareMode
{
    /// <summary>Arts6311.SquareFake, offline. The default.</summary>
    Fake,

    /// <summary>The Square sandbox, with developer-supplied credentials and a dev tunnel.</summary>
    Sandbox,
}

internal static class SquareModes
{
    public const string ConfigurationKey = "Square:Mode";

    /// <summary>
    /// Parses <c>Square:Mode</c>. Missing means <see cref="SquareMode.Fake"/>, so a clean clone runs offline.
    /// Anything other than <c>fake</c> or <c>sandbox</c> stops the AppHost: there is no production mode in run
    /// mode, and a typo must not silently fall back to a different Square.
    /// </summary>
    public static SquareMode Parse(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        null or "" or "FAKE" => SquareMode.Fake,
        "SANDBOX" => SquareMode.Sandbox,
        _ => throw new InvalidOperationException(
            $"{ConfigurationKey} must be 'fake' or 'sandbox' (was '{value}')."),
    };
}
