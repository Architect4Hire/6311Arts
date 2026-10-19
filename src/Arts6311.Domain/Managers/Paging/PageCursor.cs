using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Arts6311.Domain.Managers.Results;

namespace Arts6311.Domain.Managers.Paging;

/// <summary>
/// The position of the last row on a page, as a keyset: the value the list is ordered by and the row's unique
/// key to break ties. Ported from creatorpantry's <c>ReferenceCursor</c>.
/// </summary>
/// <remarks>
/// <para>
/// A keyset, not an offset: <c>OFFSET</c> re-counts every earlier row and skips or repeats rows when the set
/// changes between requests; "resume after this exact row" does neither. The tie-breaker matters because sort
/// values (names, dates) are not unique.
/// </para>
/// <para>
/// The encoded cursor carries a fingerprint of the query <c>scope</c> (resource plus filters), so a cursor
/// replayed against a different list fails as <see cref="ErrorCodes.CursorInvalid"/> instead of returning the
/// wrong page. It is a checksum, not a signature: everything in it came from a row the caller just read.
/// Page size is deliberately not part of the scope.
/// </para>
/// <para>Opaque by construction (base64url), so its layout can change without breaking clients.</para>
/// </remarks>
public sealed record PageCursor(string SortValue, string TieBreaker)
{
    // A control character, so it cannot occur in a display name or a key.
    private const char Separator = '\u001F';
    private const int FingerprintBytes = 8;

    public string Encode(string scope)
    {
        ArgumentException.ThrowIfNullOrEmpty(scope);

        var text = string.Join(Separator, SortValue, TieBreaker, Fingerprint(scope));
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(text));
    }

    public static OperationResult<PageCursor> Decode(string cursor, string scope)
    {
        ArgumentException.ThrowIfNullOrEmpty(scope);

        var invalid = OperationResult.Failure<PageCursor>(
            OperationError.Of(ErrorCodes.CursorInvalid, "The cursor is not valid for this list."));

        if (string.IsNullOrEmpty(cursor))
        {
            return invalid;
        }

        string text;
        try
        {
            text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
        }
        catch (FormatException)
        {
            return invalid;
        }

        var parts = text.Split(Separator);
        if (parts.Length != 3 || parts[1].Length == 0 || !string.Equals(parts[2], Fingerprint(scope), StringComparison.Ordinal))
        {
            return invalid;
        }

        return OperationResult.Success(new PageCursor(parts[0], parts[1]));
    }

    private static string Fingerprint(string scope) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(scope)).AsSpan(0, FingerprintBytes));
}
