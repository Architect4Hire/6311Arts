using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Shop.Data;
using Arts6311.Domain.Modules.Shop.Data.Repositories;
using Arts6311.Domain.Modules.Shop.Managers;
using Arts6311.Tests.Shop.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Shop;

/// <summary>The repository's queries and the tables' constraints, on SQLite as the rest of the suite runs.</summary>
public sealed class ShopRepositoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheListIsByTitleAndAKeysetResumesExactlyAfterTheLastRow()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        await host.AddItemAsync("Serpentine Pendant", itemNumber: 1, sku: "ART-1");
        await host.AddItemAsync("Copper Cuff", itemNumber: 2, sku: "ART-2");
        await host.AddItemAsync("Twin", itemNumber: 3, sku: "ART-3");
        await host.AddItemAsync("Twin", itemNumber: 4, sku: "ART-4");
        await host.AddItemAsync("Lodolite Pendant", itemNumber: 5, sku: "ART-5");

        var first = await ListAsync(host, after: null, take: 3);
        Assert.Equal(["Copper Cuff", "Lodolite Pendant", "Serpentine Pendant"], first.Select(row => row.Title));

        var second = await ListAsync(host, new ShopItemListPosition(first[^1].Title, first[^1].Id), take: 3);
        Assert.Equal(["Twin", "Twin"], second.Select(row => row.Title));

        // The twins tie on title; the id breaks the tie, so resuming after either never repeats or skips the other.
        var afterFirstTwin = await ListAsync(host, new ShopItemListPosition(second[0].Title, second[0].Id), take: 10);
        Assert.Equal(second[1].Id, Assert.Single(afterFirstTwin).Id);
    }

    [Fact]
    public async Task SearchIgnoresCaseAndTreatsWildcardsAsTextAndStatusAndCategoryFilter()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        await host.AddItemAsync("Copper Cuff", PublishStatus.Published, 1, "ART-1", null, "jewelry");
        await host.AddItemAsync("Large Branch Handle Bowl", itemNumber: 2, sku: "ART-2", categories: "ceramics");
        await host.AddItemAsync("100% Cotton Tote", itemNumber: 3, sku: "ART-3");
        await host.AddItemAsync("Under_score", PublishStatus.Archived, 4, "ART-4");

        Assert.Equal(["Copper Cuff"], (await ListAsync(host, null, 10, search: "CUFF")).Select(row => row.Title));
        Assert.Equal(["100% Cotton Tote"], (await ListAsync(host, null, 10, search: "%")).Select(row => row.Title));
        Assert.Equal(["Under_score"], (await ListAsync(host, null, 10, search: "_")).Select(row => row.Title));
        Assert.Equal(["Copper Cuff"], (await ListAsync(host, null, 10, PublishStatus.Published)).Select(row => row.Title));
        Assert.Equal(
            ["Large Branch Handle Bowl"],
            (await ListAsync(host, null, 10, category: "ceramics")).Select(row => row.Title));
    }

    [Fact]
    public async Task ARowCarriesItsCategoriesItsLeadPhotographAndWhichArtistToAskAbout()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        var artist = await host.AddArtistAsync("Carrie Haase");
        var id = await host.AddItemAsync("Copper Cuff", itemNumber: 1, sku: "ART-1", artistId: artist, categories: ["jewelry", "sculpture"]);
        var front = await host.AddImageAsync("Front.");
        var side = await host.AddImageAsync("Side.");
        await host.ExecuteAsync(async context =>
        {
            context.Add(new Domain.Modules.Shop.Data.Entities.ShopItemImage { ShopItemId = id, MediaAssetId = side, Position = 1 });
            context.Add(new Domain.Modules.Shop.Data.Entities.ShopItemImage { ShopItemId = id, MediaAssetId = front, Position = 0 });
            await context.SaveChangesAsync(Ct);
        });

        var row = Assert.Single(await ListAsync(host, null, 10));

        Assert.Equal(["jewelry", "sculpture"], row.Categories.Order(StringComparer.Ordinal));
        Assert.Equal(front, row.LeadImageMediaId);
        // The row carries the id, never the name: describing the artist is the facade's (ADR 0033).
        Assert.Equal(artist, row.ArtistId);
    }

    [Fact]
    public async Task TheNextItemNumberIsOnePastTheHighestInUseAndStartsAtOne()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);

        var onEmpty = await WithRepositoryAsync(host, repository => repository.NextItemNumberAsync(Ct));
        await host.AddItemAsync("Copper Cuff", itemNumber: 9, sku: "ART-9");
        var afterNine = await WithRepositoryAsync(host, repository => repository.NextItemNumberAsync(Ct));

        Assert.Equal(1, onEmpty);
        Assert.Equal(10, afterNine);
    }

    [Fact]
    public async Task TheRegistryRefusesTwoRecordsHoldingOneSku()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        await host.AddItemAsync("First", itemNumber: 1, sku: "SHARED-1");

        await Assert.ThrowsAnyAsync<Exception>(() => host.AddItemAsync("Second", itemNumber: 2, sku: "SHARED-1"));
    }

    private static Task<List<ShopItemListRow>> ListAsync(
        ShopTestHost host,
        ShopItemListPosition? after,
        int take,
        PublishStatus? status = null,
        string? search = null,
        string? category = null) =>
        WithRepositoryAsync(host, repository => repository.ListAsync(after, take, status, search, category, Ct));

    private static async Task<T> WithRepositoryAsync<T>(ShopTestHost host, Func<IShopRepository, Task<T>> query)
    {
        using var scope = host.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<IShopRepository>());
    }
}
