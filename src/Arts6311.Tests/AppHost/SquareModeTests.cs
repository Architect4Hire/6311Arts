using Arts6311.AppHost;

namespace Arts6311.Tests.AppHost;

public sealed class SquareModeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fake")]
    [InlineData(" Fake ")]
    public void MissingOrFakeMeansFake(string? value)
    {
        Assert.Equal(SquareMode.Fake, SquareModes.Parse(value));
    }

    [Theory]
    [InlineData("sandbox")]
    [InlineData("SANDBOX")]
    public void SandboxIsAccepted(string value)
    {
        Assert.Equal(SquareMode.Sandbox, SquareModes.Parse(value));
    }

    [Theory]
    [InlineData("production")]
    [InlineData("live")]
    [InlineData("sandbx")]
    public void AnythingElseStopsTheAppHost(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => SquareModes.Parse(value));

        Assert.Contains(SquareModes.ConfigurationKey, error.Message, StringComparison.Ordinal);
    }
}
