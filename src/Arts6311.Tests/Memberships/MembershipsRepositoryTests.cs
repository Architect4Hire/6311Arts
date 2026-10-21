using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Memberships.Data;
using Arts6311.Domain.Modules.Memberships.Data.Repositories;
using Arts6311.Domain.Modules.Memberships.Managers.ServiceModels;
using Arts6311.Tests.Memberships.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Memberships;

/// <summary>The repository's queries and the tables' constraints, on SQLite as the rest of the suite runs.</summary>
public sealed class MembershipsRepositoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheListIsInDisplayOrderAndAKeysetResumesExactlyAfterTheLastRow()
    {
        await using var host = await MembershipsTestHost.CreateAsync(Ct);
        await host.AddPlanAsync("Blue", displayOrder: 3);
        await host.AddPlanAsync("Gray", displayOrder: 1);
        await host.AddPlanAsync("Black", displayOrder: 2);
        await host.AddPlanAsync("Twin A", displayOrder: 4);
        await host.AddPlanAsync("Twin B", displayOrder: 4);

        var first = await ListAsync(host, after: null, take: 3);
        Assert.Equal(["Gray", "Black", "Blue"], first.Select(row => row.Name));

        var second = await ListAsync(host, new MembershipPlanListPosition(first[^1].DisplayOrder, first[^1].Id), take: 3);
        Assert.Equal(2, second.Count);

        // The twins tie on order; the id breaks the tie, so resuming after either never repeats or skips the other.
        var afterFirstTwin = await ListAsync(host, new MembershipPlanListPosition(second[0].DisplayOrder, second[0].Id), take: 10);
        Assert.Equal(second[1].Id, Assert.Single(afterFirstTwin).Id);
    }

    [Fact]
    public async Task SearchIgnoresCaseAndTreatsWildcardsAsTextAndStatusFilters()
    {
        await using var host = await MembershipsTestHost.CreateAsync(Ct);
        await host.AddPlanAsync("Gray", PublishStatus.Published, 1);
        await host.AddPlanAsync("Black", displayOrder: 2);
        await host.AddPlanAsync("100% Supporter", displayOrder: 3);
        await host.AddPlanAsync("Under_score", PublishStatus.Archived, 4);

        Assert.Equal(["Gray"], (await ListAsync(host, null, 10, search: "GRAY")).Select(row => row.Name));
        Assert.Equal(["100% Supporter"], (await ListAsync(host, null, 10, search: "%")).Select(row => row.Name));
        Assert.Equal(["Under_score"], (await ListAsync(host, null, 10, search: "_")).Select(row => row.Name));
        Assert.Equal(["Gray"], (await ListAsync(host, null, 10, PublishStatus.Published)).Select(row => row.Name));
    }

    [Fact]
    public async Task ARowCountsThePerksAndCarriesWhatSquareSaysAboutBilling()
    {
        await using var host = await MembershipsTestHost.CreateAsync(Ct);
        var id = await host.AddPlanAsync("Gray", PublishStatus.Published, 1, 10_000, "5% off every class", "Tote");
        await host.SquareReportedAsync(id, 12_000, "ANNUAL");

        var row = Assert.Single(await ListAsync(host, null, 10));

        Assert.Equal(2, row.PerkCount);
        Assert.Equal(10_000, row.StartingPriceCents);
        Assert.Equal(12_000, row.PriceCents);
        Assert.Equal("ANNUAL", row.Cadence);
        Assert.Equal("USD", row.Currency);
    }

    [Fact]
    public async Task TheNextDisplayOrderIsOnePastTheHighestInUseAndStartsAtOne()
    {
        await using var host = await MembershipsTestHost.CreateAsync(Ct);

        var onEmpty = await WithRepositoryAsync(host, repository => repository.NextDisplayOrderAsync(Ct));
        await host.AddPlanAsync("Gray", displayOrder: 9);
        var afterNine = await WithRepositoryAsync(host, repository => repository.NextDisplayOrderAsync(Ct));

        Assert.Equal(1, onEmpty);
        Assert.Equal(10, afterNine);
    }

    [Fact]
    public async Task ThePerksComeBackInOrderAndGoWithThePlan()
    {
        await using var host = await MembershipsTestHost.CreateAsync(Ct);
        var id = await host.AddPlanAsync("Blue", PublishStatus.Draft, 1, 50_000, "15% off", "Party", "Tote");

        var stored = await host.StoredAsync(id);
        await host.ExecuteAsync(async context =>
        {
            context.Remove(await context.Set<Domain.Modules.Memberships.Data.Entities.MembershipPlan>()
                .SingleAsync(plan => plan.Id == id, Ct));
            await context.SaveChangesAsync(Ct);
        });

        Assert.Equal(
            ["15% off", "Party", "Tote"],
            stored!.Perks.OrderBy(perk => perk.Position).Select(perk => perk.Text));

        // Cascade: the perks belong to the plan, so nothing is left behind.
        Assert.Empty(await host.QueryAsync(context =>
            context.Set<Domain.Modules.Memberships.Data.Entities.MembershipPlanPerk>().AsNoTracking().ToListAsync(Ct)));
    }

    [Fact]
    public async Task TwoPlansCannotShareASlug()
    {
        await using var host = await MembershipsTestHost.CreateAsync(Ct);
        var first = await host.AddPlanAsync("Gray");
        var slug = (await host.StoredAsync(first))!.Slug;

        await Assert.ThrowsAnyAsync<Exception>(() => host.ExecuteAsync(async context =>
        {
            context.Add(new Domain.Modules.Memberships.Data.Entities.MembershipPlan
            {
                Id = Guid.NewGuid(),
                Name = "Copy",
                Slug = slug,
                UpdatedBy = MembershipsTestHost.EditorId,
                RowVersion = Guid.NewGuid(),
            });
            await context.SaveChangesAsync(Ct);
        }));
    }

    private static Task<List<MembershipPlanSummaryServiceModel>> ListAsync(
        MembershipsTestHost host,
        MembershipPlanListPosition? after,
        int take,
        PublishStatus? status = null,
        string? search = null) =>
        WithRepositoryAsync(host, repository => repository.ListAsync(after, take, status, search, Ct));

    private static async Task<T> WithRepositoryAsync<T>(
        MembershipsTestHost host, Func<IMembershipsRepository, Task<T>> query)
    {
        using var scope = host.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<IMembershipsRepository>());
    }
}
