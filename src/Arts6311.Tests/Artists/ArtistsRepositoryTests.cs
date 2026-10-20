using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Artists.Data;
using Arts6311.Domain.Modules.Artists.Data.Repositories;
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
        var second = await ListAsync(host, new ArtistListPosition(first[^1].Name, first[^1].Id), take: 3);
        Assert.Equal(["Carol", "Dan"], second.Select(row => row.Name));

        var resumeAfterFirstBob = await ListAsync(host, new ArtistListPosition(first[1].Name, first[1].Id), take: 10);
        Assert.Equal(first[2].Id, resumeAfterFirstBob[0].Id);
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

    private static async Task<List<Arts6311.Domain.Modules.Artists.Managers.ServiceModels.ArtistSummaryServiceModel>> ListAsync(
        ArtistsTestHost host, ArtistListPosition? after, int take, PublishStatus? status = null, string? search = null)
    {
        using var scope = host.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IArtistsRepository>().ListAsync(after, take, status, search, Ct);
    }
}
