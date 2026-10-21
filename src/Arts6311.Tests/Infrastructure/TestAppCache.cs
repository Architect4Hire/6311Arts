using Arts6311.Domain.Managers.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Infrastructure;

/// <summary>
/// A fresh in-memory HybridCache behind <see cref="IAppCache"/>, registered exactly as <c>AddArtsKernel</c> does, for a
/// facade a test builds by hand. One per facade: a shared cache would carry one test's entries into the next.
/// </summary>
internal static class TestAppCache
{
    public static IAppCache Create() =>
        new ServiceCollection().AddArtsCaching().BuildServiceProvider().GetRequiredService<IAppCache>();
}
