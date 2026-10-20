using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Classes.Data;
using Arts6311.Domain.Modules.Classes.Data.Repositories;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;
using Arts6311.Tests.Classes.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Classes;

/// <summary>The repository's queries and the tables' constraints, on SQLite as the rest of the suite runs.</summary>
public sealed class ClassesRepositoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheListIsByTitleAndAKeysetResumesExactlyAfterTheLastRow()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        await host.AddOfferingAsync("Scratchboard", offeringNumber: 1, skus: "A-1");
        await host.AddOfferingAsync("Bookbinding", offeringNumber: 2, skus: "A-2");
        await host.AddOfferingAsync("Twin", offeringNumber: 3, skus: "A-3");
        await host.AddOfferingAsync("Twin", offeringNumber: 4, skus: "A-4");
        await host.AddOfferingAsync("Screen Printing", offeringNumber: 5, skus: "A-5");

        var first = await ListAsync(host, after: null, take: 3);
        Assert.Equal(["Bookbinding", "Scratchboard", "Screen Printing"], first.Select(row => row.Title));

        var second = await ListAsync(host, new ClassListPosition(first[^1].Title, first[^1].Id), take: 3);
        Assert.Equal(["Twin", "Twin"], second.Select(row => row.Title));

        // The twins tie on title; the id breaks the tie, so resuming after either never repeats or skips the other.
        var afterFirstTwin = await ListAsync(host, new ClassListPosition(second[0].Title, second[0].Id), take: 10);
        Assert.Equal(second[1].Id, Assert.Single(afterFirstTwin).Id);
    }

    [Fact]
    public async Task SearchIgnoresCaseAndTreatsWildcardsAsTextAndStatusFilters()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        await host.AddOfferingAsync("Screen Printing", PublishStatus.Published, 1, skus: "B-1");
        await host.AddOfferingAsync("Scratchboard", offeringNumber: 2, skus: "B-2");
        await host.AddOfferingAsync("100% Paper", offeringNumber: 3, skus: "B-3");
        await host.AddOfferingAsync("Under_score", PublishStatus.Archived, 4, skus: "B-4");

        Assert.Equal(["Screen Printing"], (await ListAsync(host, null, 10, search: "PRINT")).Select(row => row.Title));
        Assert.Equal(["100% Paper"], (await ListAsync(host, null, 10, search: "%")).Select(row => row.Title));
        Assert.Equal(["Under_score"], (await ListAsync(host, null, 10, search: "_")).Select(row => row.Title));
        Assert.Equal(["Screen Printing"], (await ListAsync(host, null, 10, PublishStatus.Published)).Select(row => row.Title));
    }

    [Fact]
    public async Task ARowCountsItsSessionsAndSaysWhenTheClassNextMeets()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var now = host.Time.GetUtcNow();
        await host.AddOfferingAsync("Course", offeringNumber: 1, firstSession: now.AddDays(-14), skus: ["C-1", "C-2", "C-3"]);
        await host.AddOfferingAsync("Finished", offeringNumber: 2, firstSession: now.AddDays(-30), skus: ["C-4"]);

        var rows = await ListAsync(host, null, 10, asOf: now);

        var course = rows.Single(row => row.Title == "Course");
        Assert.Equal(3, course.SessionCount);

        // Two of the three have passed; the third is a week out.
        Assert.Equal(now.AddDays(-14).AddDays(14), course.NextSessionStartsAt);
        Assert.Null(rows.Single(row => row.Title == "Finished").NextSessionStartsAt);
    }

    [Fact]
    public async Task TheNextOfferingNumberIsOnePastTheHighestInUseAndStartsAtOne()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);

        var onEmpty = await WithRepositoryAsync(host, repository => repository.NextOfferingNumberAsync(Ct));
        await host.AddOfferingAsync("Course", offeringNumber: 9, skus: "D-1");
        var afterNine = await WithRepositoryAsync(host, repository => repository.NextOfferingNumberAsync(Ct));

        Assert.Equal(1, onEmpty);
        Assert.Equal(10, afterNine);
    }

    [Fact]
    public async Task TheRegistryRefusesTwoRecordsHoldingOneSku()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        await host.AddOfferingAsync("First", offeringNumber: 1, skus: "SHARED-1");

        await Assert.ThrowsAnyAsync<Exception>(() => host.AddOfferingAsync("Second", offeringNumber: 2, skus: "SHARED-1"));
    }

    private static Task<List<ClassSummaryServiceModel>> ListAsync(
        ClassesTestHost host,
        ClassListPosition? after,
        int take,
        PublishStatus? status = null,
        string? search = null,
        DateTimeOffset? asOf = null) =>
        WithRepositoryAsync(host, repository => repository.ListAsync(
            after, take, status, search, asOf ?? host.Time.GetUtcNow(), Ct));

    private static async Task<T> WithRepositoryAsync<T>(ClassesTestHost host, Func<IClassesRepository, Task<T>> query)
    {
        using var scope = host.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<IClassesRepository>());
    }
}
