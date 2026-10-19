using System.Buffers.Text;
using System.Text;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Results;

namespace Arts6311.Tests.Kernel;

public sealed class PagingTests
{
    private const string Scope = "artists?status=published";

    [Fact]
    public void ACursorRoundTripsWithinItsScope()
    {
        var cursor = new PageCursor("Jane Doe", "jane-doe");

        var decoded = PageCursor.Decode(cursor.Encode(Scope), Scope);

        Assert.True(decoded.Succeeded);
        Assert.Equal(cursor, decoded.Value);
    }

    [Fact]
    public void ACursorFromAnotherListIsRejected()
    {
        var encoded = new PageCursor("Jane Doe", "jane-doe").Encode(Scope);

        var decoded = PageCursor.Decode(encoded, "artists?status=draft");

        Assert.Equal(ErrorCodes.CursorInvalid, decoded.Error?.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAAA")]
    public void GarbageIsRejectedNotThrown(string cursor)
    {
        Assert.Equal(ErrorCodes.CursorInvalid, PageCursor.Decode(cursor, Scope).Error?.Code);
    }

    [Fact]
    public void AnEditedCursorIsRejected()
    {
        var forged = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("Zed\u001Fzed\u001Fnot-the-fingerprint"));

        Assert.Equal(ErrorCodes.CursorInvalid, PageCursor.Decode(forged, Scope).Error?.Code);
    }

    [Theory]
    [InlineData(null, PageRequest.DefaultLimit)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(50, 50)]
    [InlineData(10_000, PageRequest.MaxLimit)]
    public void ThePageSizeIsClamped(int? requested, int expected)
    {
        Assert.Equal(expected, new PageRequest(null, requested).EffectiveLimit);
    }
}
