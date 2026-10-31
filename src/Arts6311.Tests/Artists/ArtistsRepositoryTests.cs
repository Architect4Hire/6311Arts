using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Artists.Data;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Artists.Data.Repositories;
using Arts6311.Domain.Modules.Artists.Managers;
using Arts6311.Tests.Artists.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Artists;

/// <summary>
/// The repository's queries and the table's constraints, on SQLite as the rest of the suite runs (ADR 0030
/// follow-ups: no SQL Server test container yet).
/// </summary>
public sealed class ArtistsRepositoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheListIsByNameAndAKeysetResumesExactlyAfterTheLastRow()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        foreach (var name in new[] { "Carol", "Alice", "Bob", "Bob", "Dan" })
        {
            await host.AddArtistAsync(name, $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}");
        }

        var first = await ListAsync(host, after: null, take: 3);
        Assert.Equal(["Alice", "Bob", "Bob"], first.Select(row => row.Name));

        // The two Bobs tie on name; the id breaks the tie, so resuming after the second never repeats or skips one.
        var second = await ListAsync(host, new ListPosition(first[^1].Name, first[^1].Id), take: 3);
        Assert.Equal(["Carol", "Dan"], second.Select(row => row.Name));

        var resumeAfterFirstBob = await ListAsync(host, new ListPosition(first[1].Name, first[1].Id), take: 10);
        Assert.Equal(first[2].Id, resumeAfterFirstBob[0].Id);
    }

    [Theory]
    [InlineData("medium", new[] { "Bea", "Cy", "Al", "Di" })] // no medium last
    [InlineData("-medium", new[] { "Al", "Cy", "Bea", "Di" })]
    [InlineData("status", new[] { "Di", "Al", "Bea", "Cy" })] // Draft, Scheduled, Published, Archived
    [InlineData("-status", new[] { "Cy", "Bea", "Al", "Di" })]
    [InlineData("showing", new[] { "Bea", "Di", "Al", "Cy" })]
    [InlineData("-showing", new[] { "Al", "Cy", "Bea", "Di" })]
    [InlineData("updated", new[] { "Cy", "Al", "Bea", "Di" })]
    [InlineData("-updated", new[] { "Di", "Bea", "Al", "Cy" })]
    public async Task EachColumnSortsOnTheDatabaseAndPagesWithoutSkippingOrRepeating(string sort, string[] expected)
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var now = DateTimeOffset.UtcNow;
        await host.ExecuteAsync(async context =>
        {
            context.AddRange(
                With(host.NewArtist("Al", "al", PublishStatus.Scheduled), 1, "Weaving", active: true, now.AddDays(-2)),
                With(host.NewArtist("Bea", "bea", PublishStatus.Published), 2, "Clay", active: false, now.AddDays(-1)),
                With(host.NewArtist("Cy", "cy", PublishStatus.Archived), 3, "Paint", active: true, now.AddDays(-3)),
                With(host.NewArtist("Di", "di", PublishStatus.Draft), 4, null, active: false, now));
            await context.SaveChangesAsync(Ct);
        });
        var order = ArtistListOrder.Columns.Resolve(sort)!;

        var whole = await ListAsync(host, after: null, take: 10, sort: order);

        // One row a page, each resuming from the cursor's position as Business decodes it.
        var walked = new List<string>();
        ListPosition? after = null;
        while (await ListAsync(host, after, take: 1, sort: order) is [var row])
        {
            walked.Add(row.Name);
            after = ArtistListOrder.Columns.Decode(order, ArtistListOrder.Columns.Encode(order, row, "s"), "s").Value;
        }

        Assert.Equal(expected, whole.Select(row => row.Name));
        Assert.Equal(expected, walked);
    }

    [Fact]
    public async Task SearchIgnoresCaseAndTreatsWildcardsAsText()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        await host.AddArtistAsync("Carrie Haase", "carrie-haase");
        await host.AddArtistAsync("Harry Sudman", "harry-sudman");
        await host.AddArtistAsync("100% Clay", "100-clay");
        await host.AddArtistAsync("Under_score", "under-score");

        Assert.Equal(["Carrie Haase"], (await ListAsync(host, null, 10, search: "HAASE")).Select(row => row.Name));
        Assert.Equal(["Carrie Haase", "Harry Sudman"], (await ListAsync(host, null, 10, search: "rr")).Select(row => row.Name));
        Assert.Equal(["100% Clay"], (await ListAsync(host, null, 10, search: "%")).Select(row => row.Name));
        Assert.Equal(["Under_score"], (await ListAsync(host, null, 10, search: "_")).Select(row => row.Name));
    }

    [Fact]
    public async Task TheStatusFilterKeepsOnlyThatStatus()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        await host.AddArtistAsync("Draft One", "draft-one");
        await host.AddArtistAsync("Live One", "live-one", PublishStatus.Published);
        await host.AddArtistAsync("Gone One", "gone-one", PublishStatus.Archived);

        var published = await ListAsync(host, null, 10, PublishStatus.Published);

        Assert.Equal(["Live One"], published.Select(row => row.Name));
        Assert.Equal(PublishStatus.Published, published[0].Status);
    }

    [Fact]
    public async Task TwoArtistsCannotShareASlug()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        await host.AddArtistAsync("Jane Doe", "jane-doe");

        await Assert.ThrowsAsync<DbUpdateException>(() => host.AddArtistAsync("Jane Doe (2)", "jane-doe"));
    }

    [Fact]
    public async Task TheLegacyIdIsUniqueWherePresentAndOptionalOtherwise()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        await host.AddArtistAsync("No Legacy A", "no-legacy-a");
        await host.AddArtistAsync("No Legacy B", "no-legacy-b");
        await host.AddArtistAsync("Imported", "imported", legacyId: 42);

        await Assert.ThrowsAsync<DbUpdateException>(() => host.AddArtistAsync("Imported Twice", "imported-twice", legacyId: 42));
    }

    [Fact]
    public async Task SlugExistsLooksPastTheArtistItself()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = host.NewArtist("Jane Doe", "jane-doe");
        await host.ExecuteAsync(async context =>
        {
            context.Add(artist);
            await context.SaveChangesAsync(Ct);
        });

        using var scope = host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IArtistsRepository>();

        Assert.True(await repository.SlugExistsAsync("jane-doe", exceptId: null, Ct));
        Assert.False(await repository.SlugExistsAsync("jane-doe", artist.Id, Ct));
        Assert.False(await repository.SlugExistsAsync("john-doe", exceptId: null, Ct));
    }

    [Fact]
    public async Task APortraitMustBeARealMediaAsset()
    {
        // The Restrict foreign key media.md requires: the database itself refuses a dangling portrait.
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = host.NewArtist("Jane Doe", "jane-doe");
        artist.PortraitMediaId = Guid.NewGuid();

        await Assert.ThrowsAsync<DbUpdateException>(() => host.ExecuteAsync(async context =>
        {
            context.Add(artist);
            await context.SaveChangesAsync(Ct);
        }));
    }

    // Ids in name order, so a tie (two artists showing) breaks the same way on every run.
    private static Artist With(Artist artist, int id, string? medium, bool active, DateTimeOffset updatedAt)
    {
        artist.Id = new Guid($"00000000-0000-0000-0000-{id:D12}");
        artist.Medium = medium;
        artist.Active = active;
        artist.UpdatedAt = updatedAt;
        return artist;
    }

    private static async Task<List<Arts6311.Domain.Modules.Artists.Managers.ServiceModels.ArtistSummaryServiceModel>> ListAsync(
        ArtistsTestHost host, ListPosition? after, int take, PublishStatus? status = null, string? search = null, ListSort? sort = null)
    {
        using var scope = host.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IArtistsRepository>()
            .ListAsync(sort ?? ArtistListOrder.Columns.Default, after, take, status, search, Ct);
    }
}
