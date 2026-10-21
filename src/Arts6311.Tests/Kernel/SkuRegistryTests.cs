using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Managers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Tests.Kernel;

/// <summary>
/// The shared SKU registry (ADR 0037): one SKU across every catalog type, claimed in the same save as the row that
/// carries it.
/// </summary>
public sealed class SkuRegistryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AReservationIsOnlyRealOnceTheCallersSaveCommits()
    {
        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);
        await using var context = database.NewContext();
        var registry = Registry(context);

        registry.Reserve("CLS-1-1", CatalogOwnerTypes.ClassRun, Guid.NewGuid());
        Assert.Empty(await Stored(database));

        await context.SaveChangesAsync(Ct);
        Assert.Equal(["CLS-1-1"], (await Stored(database)).Select(row => row.Sku));
    }

    [Fact]
    public async Task TwoRecordsCannotHoldOneSkuWhateverTheirType()
    {
        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);
        await using var context = database.NewContext();
        var registry = Registry(context);

        registry.Reserve("ART-1", CatalogOwnerTypes.ShopItem, Guid.NewGuid());
        await context.SaveChangesAsync(Ct);

        await using var second = database.NewContext();
        Registry(second).Reserve("ART-1", CatalogOwnerTypes.ClassRun, Guid.NewGuid());

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => second.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task OneRecordCannotHoldTwoSkus()
    {
        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);
        await using var context = database.NewContext();
        var owner = Guid.NewGuid();
        var registry = Registry(context);

        registry.Reserve("CLS-1-1", CatalogOwnerTypes.ClassRun, owner);
        registry.Reserve("CLS-1-2", CatalogOwnerTypes.ClassRun, owner);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task ASkuReleasedAndTakenAgainInOneSaveEndsWithTheNewOwner()
    {
        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);
        var moving = Guid.NewGuid();
        var taking = Guid.NewGuid();

        await using (var setup = database.NewContext())
        {
            Registry(setup).Reserve("CLS-1-1", CatalogOwnerTypes.ClassRun, moving);
            await setup.SaveChangesAsync(Ct);
        }

        await using (var context = database.NewContext())
        {
            var registry = Registry(context);
            await registry.ReleaseAsync("CLS-1-1", new SkuOwner(CatalogOwnerTypes.ClassRun, moving), Ct);
            registry.Reserve("CLS-1-1", CatalogOwnerTypes.ClassRun, taking);
            await context.SaveChangesAsync(Ct);
        }

        var row = Assert.Single(await Stored(database));
        Assert.Equal(("CLS-1-1", taking), (row.Sku, row.OwnerId));
    }

    [Fact]
    public async Task ASkuReservedAndReleasedInOneSaveIsNeverWritten()
    {
        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);
        await using var context = database.NewContext();
        var registry = Registry(context);

        var owner = new SkuOwner(CatalogOwnerTypes.ClassRun, Guid.NewGuid());
        registry.Reserve("CLS-1-1", owner.OwnerType, owner.OwnerId);
        await registry.ReleaseAsync("CLS-1-1", owner, Ct);
        await context.SaveChangesAsync(Ct);

        Assert.Empty(await Stored(database));
    }

    [Fact]
    public async Task OwnersAnswersWhoHoldsWhatAndIsTakenIgnoresTheOwnerAsking()
    {
        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);
        var mine = Guid.NewGuid();

        await using (var setup = database.NewContext())
        {
            Registry(setup).Reserve("CLS-1-1", CatalogOwnerTypes.ClassRun, mine);
            await setup.SaveChangesAsync(Ct);
        }

        await using var context = database.NewContext();
        var registry = Registry(context);
        var owners = await registry.OwnersAsync(["CLS-1-1", "CLS-9-9"], Ct);

        Assert.Equal([new SkuOwner(CatalogOwnerTypes.ClassRun, mine)], owners.Values);
        Assert.Equal(["CLS-1-1"], owners.Keys);
        Assert.False(await registry.IsTakenAsync("CLS-1-1", new SkuOwner(CatalogOwnerTypes.ClassRun, mine), Ct));
        Assert.True(await registry.IsTakenAsync("CLS-1-1", new SkuOwner(CatalogOwnerTypes.ClassRun, Guid.NewGuid()), Ct));
        Assert.False(await registry.IsTakenAsync("CLS-9-9", new SkuOwner(CatalogOwnerTypes.ClassRun, mine), Ct));
    }

    [Fact]
    public async Task ReleasingASkuNoRowHoldsOrAnotherRecordHoldsChangesNothing()
    {
        // A release that deleted nothing would fail the caller's whole save as a concurrency conflict, and one that
        // ignored the owner would delete another record's reservation (ADR 0037).
        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);
        var theirs = new SkuOwner(CatalogOwnerTypes.ShopItem, Guid.NewGuid());

        await using (var setup = database.NewContext())
        {
            Registry(setup).Reserve("ART-1", theirs.OwnerType, theirs.OwnerId);
            await setup.SaveChangesAsync(Ct);
        }

        await using var context = database.NewContext();
        var registry = Registry(context);
        var mine = new SkuOwner(CatalogOwnerTypes.ClassRun, Guid.NewGuid());

        await registry.ReleaseAsync("CLS-9-9", mine, Ct);
        await registry.ReleaseAsync("ART-1", mine, Ct);
        await context.SaveChangesAsync(Ct);

        Assert.Equal(["ART-1"], (await Stored(database)).Select(row => row.Sku));
    }

    [Fact]
    public async Task ASkuOutsideTheShapeIsRefusedBeforeTheDatabaseSeesIt()
    {
        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);
        await using var context = database.NewContext();
        var registry = Registry(context);

        Assert.Throws<ArgumentException>(() =>
            registry.Reserve("cls 1 1", CatalogOwnerTypes.ClassRun, Guid.NewGuid()));
        Assert.Empty(await Stored(database));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  cls-4-1 ", "CLS-4-1")]
    [InlineData("   ", null)]
    public void NormalizeTrimsAndUpperCases(string? input, string? expected) =>
        Assert.Equal(expected, SkuPolicy.Normalize(input));

    [Fact]
    public void ASuggestionIsThePrefixTheOfferingAndTheSessionNumber()
    {
        Assert.Equal("CLS-4-2", SkuPolicy.ForClassRun(4, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => SkuPolicy.ForClassRun(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SkuPolicy.ForClassRun(1, 0));
    }

    private static SkuRegistry Registry(ArtsDbContext context) => new(context, new FixedSkuClock());

    private static async Task<List<CatalogSku>> Stored(SqliteArtsDatabase database)
    {
        await using var context = database.NewContext();
        return await context.Set<CatalogSku>().AsNoTracking().OrderBy(row => row.Sku).ToListAsync(Ct);
    }

    private sealed class FixedSkuClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
