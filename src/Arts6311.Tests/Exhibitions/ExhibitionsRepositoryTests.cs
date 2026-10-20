using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Exhibitions.Data;
using Arts6311.Domain.Modules.Exhibitions.Data.Entities;
using Arts6311.Domain.Modules.Exhibitions.Data.Repositories;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;
using Arts6311.Tests.Exhibitions.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Exhibitions;

/// <summary>The repository's queries and the tables' constraints, on SQLite as the rest of the suite runs.</summary>
public sealed class ExhibitionsRepositoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheListIsNewestShowFirstAndAKeysetResumesExactlyAfterTheLastRow()
    {
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct);
        await host.AddExhibitionAsync("Spring", new DateOnly(2026, 3, 1));
        await host.AddExhibitionAsync("Autumn", new DateOnly(2026, 9, 1));
        await host.AddExhibitionAsync("Twin A", new DateOnly(2026, 6, 1));
        await host.AddExhibitionAsync("Twin B", new DateOnly(2026, 6, 1));
        await host.AddExhibitionAsync("Winter", new DateOnly(2025, 12, 1));

        var first = await ListAsync(host, after: null, take: 3);
        Assert.Equal("Autumn", first[0].Title);
        Assert.Equal([new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)], first[1..].Select(row => row.StartDate));

        // The twins tie on date; the id breaks the tie, so resuming after either never repeats or skips the other.
        var second = await ListAsync(host, new ExhibitionListPosition(first[^1].StartDate, first[^1].Id), take: 3);
        Assert.Equal(["Spring", "Winter"], second.Select(row => row.Title));

        var afterFirstTwin = await ListAsync(host, new ExhibitionListPosition(first[1].StartDate, first[1].Id), take: 10);
        Assert.Equal(first[2].Id, afterFirstTwin[0].Id);
    }

    [Fact]
    public async Task SearchIgnoresCaseAndTreatsWildcardsAsTextAndStatusFilters()
    {
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct);
        var day = new DateOnly(2026, 1, 1);
        await host.AddExhibitionAsync("Boats & Pianos", day, PublishStatus.Published);
        await host.AddExhibitionAsync("Portrait Mode", day.AddDays(1));
        await host.AddExhibitionAsync("100% Paper", day.AddDays(2));
        await host.AddExhibitionAsync("Under_score", day.AddDays(3), PublishStatus.Archived);

        Assert.Equal(["Boats & Pianos"], (await ListAsync(host, null, 10, search: "PIANO")).Select(row => row.Title));
        Assert.Equal(["100% Paper"], (await ListAsync(host, null, 10, search: "%")).Select(row => row.Title));
        Assert.Equal(["Under_score"], (await ListAsync(host, null, 10, search: "_")).Select(row => row.Title));
        Assert.Equal(["Boats & Pianos"], (await ListAsync(host, null, 10, PublishStatus.Published)).Select(row => row.Title));
    }

    [Fact]
    public async Task ARowCountsItsCreditsAndTheCreditsComeBackInOrder()
    {
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct);
        var zed = await host.AddArtistAsync("Zed Artist");
        var amy = await host.AddArtistAsync("Amy Artist");
        var id = await host.AddExhibitionAsync("Group Show", new DateOnly(2026, 2, 1), PublishStatus.Draft, zed, amy);

        var row = Assert.Single(await ListAsync(host, null, 10));
        Assert.Equal(2, row.ArtistCount);

        using var scope = host.CreateScope();
        var tracked = await scope.ServiceProvider.GetRequiredService<IExhibitionsRepository>().FindAsync(id, Ct);
        Assert.Equal([zed, amy], tracked!.Artists.OrderBy(credit => credit.Position).Select(credit => credit.ArtistId));
    }

    [Fact]
    public async Task TheSlugIsUniqueInTheTableAsWellAsInBusiness()
    {
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct);
        var day = new DateOnly(2026, 1, 1);
        await host.AddExhibitionAsync("Queue", "queue", day, PublishStatus.Draft, []);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            host.AddExhibitionAsync("Queue again", "queue", day, PublishStatus.Draft, []));
    }

    [Fact]
    public async Task ACreditedArtistCannotBeDeletedAndAnExhibitionTakesItsCreditsWithIt()
    {
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct);
        var jane = await host.AddArtistAsync("Jane Doe");
        var id = await host.AddExhibitionAsync("Solo", new DateOnly(2026, 2, 1), PublishStatus.Draft, jane);

        // Restrict: the credit holds the artist in place.
        await Assert.ThrowsAsync<DbUpdateException>(() => host.ExecuteAsync(async context =>
        {
            context.Remove(await context.Set<Artist>().SingleAsync(artist => artist.Id == jane, Ct));
            await context.SaveChangesAsync(Ct);
        }));

        // Cascade: the credits belong to the exhibition.
        await host.ExecuteAsync(async context =>
        {
            context.Remove(await context.Set<Exhibition>().SingleAsync(exhibition => exhibition.Id == id, Ct));
            await context.SaveChangesAsync(Ct);
        });
        Assert.Equal(0, await host.QueryAsync(context => context.Set<ExhibitionArtist>().CountAsync(Ct)));
    }

    private static async Task<List<ExhibitionSummaryServiceModel>> ListAsync(
        ExhibitionsTestHost host, ExhibitionListPosition? after, int take, PublishStatus? status = null, string? search = null)
    {
        using var scope = host.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IExhibitionsRepository>().ListAsync(after, take, status, search, Ct);
    }
}
