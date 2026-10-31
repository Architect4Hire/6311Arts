using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Instructors.Data;
using Arts6311.Domain.Modules.Instructors.Data.Repositories;
using Arts6311.Domain.Modules.Instructors.Managers;
using Arts6311.Tests.Instructors.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Instructors;

/// <summary>
/// The repository's queries and the table's constraints, on SQLite as the rest of the suite runs (ADR 0030
/// follow-ups: no SQL Server test container yet).
/// </summary>
public sealed class InstructorsRepositoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheListIsByNameAndAKeysetResumesExactlyAfterTheLastRow()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        foreach (var name in new[] { "Carol", "Alice", "Bob", "Bob", "Dan" })
        {
            await host.AddInstructorAsync(name, $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}");
        }

        var first = await ListAsync(host, after: null, take: 3);
        Assert.Equal(["Alice", "Bob", "Bob"], first.Select(row => row.Name));

        // The two Bobs tie on name; the id breaks the tie, so resuming after the second never repeats or skips one.
        var second = await ListAsync(host, new ListPosition(first[^1].Name, first[^1].Id), take: 3);
        Assert.Equal(["Carol", "Dan"], second.Select(row => row.Name));

        var resumeAfterFirstBob = await ListAsync(host, new ListPosition(first[1].Name, first[1].Id), take: 10);
        Assert.Equal(first[2].Id, resumeAfterFirstBob[0].Id);
    }

    [Fact]
    public async Task SearchIgnoresCaseAndTreatsWildcardsAsText()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        await host.AddInstructorAsync("Carrie Haase", "carrie-haase");
        await host.AddInstructorAsync("Harry Sudman", "harry-sudman");
        await host.AddInstructorAsync("100% Clay", "100-clay");
        await host.AddInstructorAsync("Under_score", "under-score");

        Assert.Equal(["Carrie Haase"], (await ListAsync(host, null, 10, search: "HAASE")).Select(row => row.Name));
        Assert.Equal(["Carrie Haase", "Harry Sudman"], (await ListAsync(host, null, 10, search: "rr")).Select(row => row.Name));
        Assert.Equal(["100% Clay"], (await ListAsync(host, null, 10, search: "%")).Select(row => row.Name));
        Assert.Equal(["Under_score"], (await ListAsync(host, null, 10, search: "_")).Select(row => row.Name));
    }

    [Fact]
    public async Task TheStatusFilterKeepsOnlyThatStatus()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        await host.AddInstructorAsync("Draft One", "draft-one");
        await host.AddInstructorAsync("Live One", "live-one", PublishStatus.Published);
        await host.AddInstructorAsync("Gone One", "gone-one", PublishStatus.Archived);

        var published = await ListAsync(host, null, 10, PublishStatus.Published);

        Assert.Equal(["Live One"], published.Select(row => row.Name));
        Assert.Equal(PublishStatus.Published, published[0].Status);
    }

    [Fact]
    public async Task TwoInstructorsCannotShareASlug()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        await host.AddInstructorAsync("Jane Doe", "jane-doe");

        await Assert.ThrowsAsync<DbUpdateException>(() => host.AddInstructorAsync("Jane Doe (2)", "jane-doe"));
    }

    [Fact]
    public async Task TheLegacyIdIsUniqueWherePresentAndOptionalOtherwise()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        await host.AddInstructorAsync("No Legacy A", "no-legacy-a");
        await host.AddInstructorAsync("No Legacy B", "no-legacy-b");
        await host.AddInstructorAsync("Imported", "imported", legacyId: 42);

        await Assert.ThrowsAsync<DbUpdateException>(() => host.AddInstructorAsync("Imported Twice", "imported-twice", legacyId: 42));
    }

    [Fact]
    public async Task SlugExistsLooksPastTheInstructorItself()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var instructor = host.NewInstructor("Jane Doe", "jane-doe");
        await host.ExecuteAsync(async context =>
        {
            context.Add(instructor);
            await context.SaveChangesAsync(Ct);
        });

        using var scope = host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IInstructorsRepository>();

        Assert.True(await repository.SlugExistsAsync("jane-doe", exceptId: null, Ct));
        Assert.False(await repository.SlugExistsAsync("jane-doe", instructor.Id, Ct));
        Assert.False(await repository.SlugExistsAsync("john-doe", exceptId: null, Ct));
    }

    [Fact]
    public async Task APortraitMustBeARealMediaAsset()
    {
        // The Restrict foreign key media.md requires: the database itself refuses a dangling portrait.
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var instructor = host.NewInstructor("Jane Doe", "jane-doe");
        instructor.PortraitMediaId = Guid.NewGuid();

        await Assert.ThrowsAsync<DbUpdateException>(() => host.ExecuteAsync(async context =>
        {
            context.Add(instructor);
            await context.SaveChangesAsync(Ct);
        }));
    }

    private static async Task<List<Arts6311.Domain.Modules.Instructors.Managers.ServiceModels.InstructorSummaryServiceModel>> ListAsync(
        InstructorsTestHost host, ListPosition? after, int take, PublishStatus? status = null, string? search = null)
    {
        using var scope = host.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IInstructorsRepository>().ListAsync(InstructorListOrder.Columns.Default, after, take, status, search, Ct);
    }
}
