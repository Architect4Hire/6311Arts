using Arts6311.Domain.Managers.Outbox;

namespace Arts6311.Tests.Kernel;

public sealed class OutboxPolicyTests
{
    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 40)]
    [InlineData(4, 80)]
    [InlineData(8, 1280)]
    public void BackoffDoublesFromTenSeconds(int attempts, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), OutboxPolicy.BackoffFor(attempts));
    }

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(1_000)]
    [InlineData(int.MaxValue)]
    public void BackoffIsCappedAtThirtyMinutes(int attempts)
    {
        Assert.Equal(TimeSpan.FromMinutes(30), OutboxPolicy.BackoffFor(attempts));
    }

    [Fact]
    public void EveryRetryBeforePoisonIsUncappedAndDoubles()
    {
        for (var attempts = 1; attempts < OutboxPolicy.MaxAttempts; attempts++)
        {
            Assert.Equal(OutboxPolicy.BackoffFor(attempts) * 2, OutboxPolicy.BackoffFor(attempts + 1));
        }
    }
}
