using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Artists.Managers;
using Arts6311.Domain.Modules.Classes.Managers;
using Arts6311.Domain.Modules.Exhibitions.Managers;
using Arts6311.Domain.Modules.Forms.Managers;
using Arts6311.Domain.Modules.Instructors.Data.Entities;
using Arts6311.Domain.Modules.Instructors.Managers;
using Arts6311.Domain.Modules.Memberships.Managers;
using Arts6311.Domain.Modules.Pages.Managers;
using Arts6311.Domain.Modules.Redirects.Managers;
using Arts6311.Domain.Modules.Shop.Managers;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Tests.Kernel;

/// <summary>
/// Every CMS list's every sortable column (ADR 0062), both ways, first page and resumed from a value and from an empty
/// one, translated and run by EF on the real model. <see cref="ListOrderTests"/> proves what the order means; the
/// Artists repository tests prove it pages on the database; this proves no column of any list throws when staff click
/// its header — a count over a child table, a coalesced price, a date, a flag, a name another module holds.
/// </summary>
public sealed class ListOrderTranslationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset At = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public Task Artists() => TranslatesAsync(ArtistListOrder.Columns, new Dictionary<string, object>
    {
        ["name"] = "Ann",
        ["medium"] = "Clay",
        ["status"] = PublishStatus.Published,
        ["showing"] = true,
        ["updated"] = At,
    });

    [Fact]
    public Task Instructors() => TranslatesAsync(InstructorListOrder.Columns, new Dictionary<string, object>
    {
        ["name"] = "Ann",
        ["medium"] = "Clay",
        ["status"] = PublishStatus.Draft,
        ["teaching"] = false,
        ["updated"] = At,
    });

    [Fact]
    public Task Classes() => TranslatesAsync(ClassListOrder.Columns, new Dictionary<string, object>
    {
        ["title"] = "Wheel",
        ["next"] = At,
        ["instructors"] = "Ann",
        ["status"] = PublishStatus.Scheduled,
        ["runs"] = 2,
        ["updated"] = At,
    }, database => new ClassListContext(database.Set<Instructor>(), At));

    [Fact]
    public Task Exhibitions() => TranslatesAsync(ExhibitionListOrder.Columns, new Dictionary<string, object>
    {
        ["title"] = "Spring",
        ["dates"] = new DateOnly(2026, 4, 1),
        ["status"] = PublishStatus.Archived,
        ["artists"] = 3,
        ["updated"] = At,
    });

    [Fact]
    public Task Pages() => TranslatesAsync(PageListOrder.Columns, new Dictionary<string, object>
    {
        ["title"] = "About",
        ["status"] = PublishStatus.Published,
        ["updated"] = At,
    });

    [Fact]
    public Task Memberships() => TranslatesAsync(MembershipPlanListOrder.Columns, new Dictionary<string, object>
    {
        ["order"] = 1,
        ["tier"] = "Friend",
        ["status"] = PublishStatus.Published,
        ["price"] = 5_000L,
        ["discount"] = 10,
        ["updated"] = At,
    });

    [Fact]
    public Task Shop() => TranslatesAsync(ShopItemListOrder.Columns, new Dictionary<string, object>
    {
        ["title"] = "Vase",
        ["artist"] = "Ann",
        ["status"] = PublishStatus.Draft,
        ["square"] = 5_000L,
        ["updated"] = At,
    }, database => database.Set<Artist>());

    [Fact]
    public Task Redirects() => TranslatesAsync(RedirectListOrder.Columns, new Dictionary<string, object>
    {
        ["from"] = "/a",
        ["to"] = "/b",
        ["type"] = 301,
        ["source"] = RedirectSource.Manual,
    });

    [Fact]
    public Task Submissions() => TranslatesAsync(FormsListOrder.Submissions, new Dictionary<string, object>
    {
        ["name"] = "Ann",
        ["email"] = "ann@example.com",
        ["wants"] = "exhibit",
        ["status"] = SubmissionStatus.New,
        ["pictures"] = 2,
        ["received"] = At,
    });

    [Fact]
    public Task Contacts() => TranslatesAsync(FormsListOrder.Contacts, new Dictionary<string, object>
    {
        ["from"] = "Ann",
        ["message"] = "Hello",
        ["status"] = true,
        ["received"] = At,
    });

    [Fact]
    public Task Subscribers() => TranslatesAsync(FormsListOrder.Subscribers, new Dictionary<string, object>
    {
        ["created"] = At,
        ["email"] = "ann@example.com",
        ["consented"] = At,
        ["wording"] = "v1",
        ["source"] = NewsletterSource.Signup,
    });

    /// <param name="queryContext">What the list's repository hands <c>Apply</c>, for a column that needs it.</param>
    private static async Task TranslatesAsync<TEntity, TRow>(
        ListOrder<TEntity, TRow> order,
        IReadOnlyDictionary<string, object> samples,
        Func<ArtsDbContext, object>? queryContext = null)
        where TEntity : class
    {
        // A column added without a sample here is a column nobody has run on the database; a default that is no
        // column would throw on every request that names no sort.
        Assert.Equal(order.Columns.Order(StringComparer.Ordinal), samples.Keys.Order(StringComparer.Ordinal));
        Assert.Contains(order.Default.Column, order.Columns);

        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);
        await using var context = database.NewContext();

        foreach (var (column, sample) in samples)
        {
            foreach (var sort in new[] { ListSort.Ascending(column), ListSort.Descending(column) })
            {
                foreach (var after in new ListPosition?[] { null, new(sample, Guid.NewGuid()), new(null, Guid.NewGuid()) })
                {
                    var query = order.Apply(context.Set<TEntity>().AsNoTracking(), sort, after, queryContext?.Invoke(context)).Take(5);

                    Assert.Empty(await query.ToListAsync(Ct));
                }
            }
        }
    }
}
