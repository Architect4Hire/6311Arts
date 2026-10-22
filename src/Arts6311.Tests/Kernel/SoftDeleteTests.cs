using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Tests.Kernel;

/// <summary>
/// The model rules soft delete rests on (ADR 0052), held once for every content type rather than per module: a
/// publishable record is deletable, a deleted row is invisible, and its web address is free again.
/// </summary>
public sealed class SoftDeleteTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryPublishableTypeIsSoftDeletable()
    {
        // The rule content.md states: every content type staff can publish, they can also delete. A new type that
        // forgets ISoftDeletable has no delete route and no Deleted view, and this is where that shows up.
        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);
        await using var context = database.NewContext();

        var publishable = context.Model.GetEntityTypes()
            .Where(type => type.FindProperty(nameof(Artist.Status))?.ClrType == typeof(PublishStatus))
            .Select(type => type.ClrType)
            .ToList();

        Assert.Equal(7, publishable.Count);
        Assert.All(publishable, type => Assert.True(
            typeof(ISoftDeletable).IsAssignableFrom(type),
            $"{type.Name} has a publish status but is not ISoftDeletable (ADR 0052)."));
    }

    [Fact]
    public async Task EverySoftDeletableTypeHidesDeletedRowsAndFiltersItsUniqueSlug()
    {
        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);
        await using var context = database.NewContext();

        var soft = context.Model.GetEntityTypes()
            .Where(type => typeof(ISoftDeletable).IsAssignableFrom(type.ClrType))
            .ToList();

        Assert.NotEmpty(soft);
        foreach (var type in soft)
        {
            Assert.NotEmpty(type.GetDeclaredQueryFilters());

            // A unique index is a database constraint and knows nothing of the filter above, so without this a
            // deleted record would hold its address for ever and releasing it on delete would fail at the save.
            // Every unique index, not only the slug's — the importer's legacy id is the one deliberate exception,
            // because a re-import must still match the record staff deleted (ADR 0052).
            foreach (var index in type.GetIndexes().Where(index => index.IsUnique))
            {
                var columns = index.Properties.Select(property => property.Name).ToList();
                if (columns is [nameof(Artist.LegacyWordPressId)])
                {
                    Assert.Equal("[LegacyWordPressId] IS NOT NULL", index.GetFilter());
                    continue;
                }

                Assert.Equal(SoftDelete.LiveRowsOnly, index.GetFilter());
            }
        }
    }

    [Fact]
    public async Task ADeletedRowIsInvisibleToTheSetAndItsSlugIsFreeForAnotherRow()
    {
        // The behaviour the filter and the index filter buy, against a real database. One type stands for all:
        // each module's own suite covers its lifecycle, and the configuration is held above.
        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);
        var deletedId = Guid.CreateVersion7();

        await using (var write = database.NewContext())
        {
            write.Add(NewArtist(deletedId, deleted: true));
            await write.SaveChangesAsync(Ct);
        }

        await using var context = database.NewContext();

        Assert.Empty(await context.Set<Artist>().ToListAsync(Ct));
        Assert.Null(await context.Set<Artist>().FirstOrDefaultAsync(row => row.Id == deletedId, Ct));
        Assert.NotNull(await context.Set<Artist>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.Id == deletedId, Ct));

        // The freed address: the same slug on a live row, which the unfiltered unique index would have refused.
        context.Add(NewArtist(Guid.CreateVersion7(), deleted: false));
        await context.SaveChangesAsync(Ct);

        Assert.Single(await context.Set<Artist>().ToListAsync(Ct));
        Assert.Equal(2, await context.Set<Artist>().IgnoreQueryFilters().CountAsync(Ct));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "“One” still names this artist. Take it off that record first, then delete it.")]
    [InlineData(2, "“One”, “Two” still name this artist. Take it off those records first, then delete it.")]
    [InlineData(5, "“One”, “Two”, “Three”, “Four”, “Five” and others still name this artist. Take it off those records first, then delete it.")]
    public void TheInUseRefusalNamesWhatStillHoldsTheRecord(int count, string? expected)
    {
        string[] labels = ["One", "Two", "Three", "Four", "Five"];
        var check = new ContentReferenceCheck(
            [.. labels.Take(count).Select(label => new ContentReference(SiteContentTypes.Exhibition, label))]);

        Assert.Equal(expected, check.Refusal("this artist"));
    }

    [Fact]
    public void NothingNamesARecordNoSourceAnswersFor()
    {
        Assert.Null(ContentReferenceCheck.None.Refusal("this artist"));
    }

    private static Artist NewArtist(Guid id, bool deleted) => new()
    {
        Id = id,
        Name = "Socorro Muciño",
        Slug = "socorro-mucino",
        Status = PublishStatus.Draft,
        UpdatedBy = "editor-1",
        RowVersion = Guid.NewGuid(),
        DeletedAt = deleted ? DateTimeOffset.UnixEpoch : null,
        DeletedBy = deleted ? "admin-1" : null,
    };
}
