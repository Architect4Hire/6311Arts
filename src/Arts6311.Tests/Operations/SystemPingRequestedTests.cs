using Arts6311.Domain.Modules.Operations.Managers;

namespace Arts6311.Tests.Operations;

public sealed class SystemPingRequestedTests
{
    [Fact]
    public void ThePayloadRoundTripsAndCarriesOnlyTheId()
    {
        var ping = new SystemPingRequested(Guid.NewGuid());

        var json = ping.Serialize();

        Assert.Equal($$"""{"pingId":"{{ping.PingId}}"}""", json);
        Assert.Equal(ping, SystemPingRequested.TryParse(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"pingId":"00000000-0000-0000-0000-000000000000"}""")]
    [InlineData("""{"pingId":"not-a-guid"}""")]
    public void AMalformedOrEmptyIdPayloadParsesToNull(string payloadJson)
    {
        Assert.Null(SystemPingRequested.TryParse(payloadJson));
    }
}
