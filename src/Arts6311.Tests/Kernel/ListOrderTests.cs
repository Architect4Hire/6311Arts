using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;

namespace Arts6311.Tests.Kernel;

/// <summary>
/// <see cref="ListOrder{TEntity, TRow}"/> on LINQ to objects: the keyset and ORDER BY it builds, its cursors, and
/// what it refuses. Each module's repository tests prove the same expressions translate on the database (ADR 0062).
/// </summary>
public sealed class ListOrderTests
{
    private const string Scope = "things|status=";

    private static readonly ListOrder<Thing, Thing> Order =
        new ListOrder<Thing, Thing>(thing => thing.Id, thing => thing.Id, ListSort.Ascending("name"))
            .By("name", thing => thing.Name, thing => thing.Name)
            .By("note", thing => thing.Note, thing => thing.Note)
            .By("status", thing => thing.Status, thing => thing.Status)
            .By("showing", thing => thing.Showing, thing => thing.Showing)
            .By("price", thing => thing.PriceCents, thing => thing.PriceCents)
            .By("updated", thing => thing.UpdatedAt, thing => thing.UpdatedAt)
            .By("opens", thing => thing.Opens, thing => thing.Opens);

    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.FromHours(-5));

    private static readonly Thing[] Things =
    [
        new(Id(1), "Carol", "b", PublishStatus.Published, true, 500, Now, new DateOnly(2026, 1, 1)),
        new(Id(2), "Alice", null, PublishStatus.Draft, false, null, Now.AddDays(-1), null),
        new(Id(3), "Bob", "a", PublishStatus.Archived, true, 500, Now, new DateOnly(2026, 3, 1)),
        new(Id(4), "Bob", null, PublishStatus.Scheduled, false, 100, Now.AddDays(-3), null),
        new(Id(5), "Dan", "a", PublishStatus.Draft, true, null, Now.AddDays(-2), new DateOnly(2026, 1, 1)),
        new(Id(6), "Eve", "c", PublishStatus.Published, false, 900, Now, new DateOnly(2025, 12, 1)),
    ];

    [Theory]
    [InlineData("name", new[] { 2, 3, 4, 1, 5, 6 })]
    [InlineData("-name", new[] { 6, 5, 1, 3, 4, 2 })]
    [InlineData("note", new[] { 3, 5, 1, 6, 2, 4 })] // empty notes last, whichever way
    [InlineData("-note", new[] { 6, 1, 3, 5, 2, 4 })]
    [InlineData("status", new[] { 2, 5, 4, 1, 6, 3 })] // the workflow, not the alphabet
    [InlineData("-status", new[] { 3, 1, 6, 4, 2, 5 })]
    [InlineData("showing", new[] { 2, 4, 6, 1, 3, 5 })]
    [InlineData("-showing", new[] { 1, 3, 5, 2, 4, 6 })]
    [InlineData("price", new[] { 4, 1, 3, 6, 2, 5 })]
    [InlineData("-price", new[] { 6, 1, 3, 4, 2, 5 })]
    [InlineData("-updated", new[] { 1, 3, 6, 2, 5, 4 })]
    [InlineData("opens", new[] { 6, 1, 5, 3, 2, 4 })]
    public void EachColumnOrdersItsWayWithEmptyLastAndTheIdBreakingTies(string sort, int[] expected)
    {
        var order = Order.Resolve(sort)!;

        Assert.Equal(expected.Select(Id), Order.Apply(Things.AsQueryable(), order, after: null).Select(thing => thing.Id));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("-name")]
    [InlineData("note")]
    [InlineData("-note")]
    [InlineData("status")]
    [InlineData("-status")]
    [InlineData("showing")]
    [InlineData("-showing")]
    [InlineData("price")]
    [InlineData("-price")]
    [InlineData("updated")]
    [InlineData("-updated")]
    [InlineData("opens")]
    [InlineData("-opens")]
    public void PagingOneRowAtATimeThroughTheCursorNeitherSkipsNorRepeats(string sort)
    {
        var order = Order.Resolve(sort)!;
        var whole = Order.Apply(Things.AsQueryable(), order, after: null).Select(thing => thing.Id).ToList();

        var walked = new List<Guid>();
        ListPosition? after = null;
        while (true)
        {
            var page = Order.Apply(Things.AsQueryable(), order, after).Take(1).ToList();
            if (page.Count == 0)
            {
                break;
            }

            walked.Add(page[0].Id);
            var decoded = Order.Decode(order, Order.Encode(order, page[0], Scope), Scope);
            Assert.True(decoded.Succeeded);
            after = decoded.Value;
        }

        Assert.Equal(whole, walked);
    }

    [Fact]
    public void AListThatAlwaysBrokeTiesDescendingKeepsDoingSoAndPagesThatWay()
    {
        var order = new ListOrder<Thing, Thing>(thing => thing.Id, thing => thing.Id, ListSort.Descending("updated"), SortDirection.Descending)
            .By("updated", thing => thing.UpdatedAt, thing => thing.UpdatedAt);
        var sort = order.Default;

        var whole = order.Apply(Things.AsQueryable(), sort, after: null).Select(thing => thing.Id).ToList();
        var walked = new List<Guid>();
        ListPosition? after = null;
        while (order.Apply(Things.AsQueryable(), sort, after).Take(1).ToList() is [var row])
        {
            walked.Add(row.Id);
            after = order.Decode(sort, order.Encode(sort, row, Scope), Scope).Value;
        }

        Assert.Equal([Id(6), Id(3), Id(1), Id(2), Id(5), Id(4)], whole);
        Assert.Equal(whole, walked);
    }

    [Fact]
    public void AColumnCanOrderByWhatTheQueryHandsItAndRefusesToRunWithoutIt()
    {
        // As the artwork list orders by its artist's name: another table, reached by an id, passed in by the repository.
        var owners = new[] { (Id: Id(1), Name: "Zoe"), (Id: Id(3), Name: "Ann") }.AsQueryable();
        var order = new ListOrder<Thing, Thing>(thing => thing.Id, thing => thing.Id, ListSort.Ascending("name"))
            .By("name", thing => thing.Name, thing => thing.Name)
            .By<IQueryable<(Guid Id, string Name)>, string?>(
                "owner",
                (thing, people) => people.Where(person => person.Id == thing.Id).Select(person => (string?)person.Name).FirstOrDefault(),
                thing => thing.Note);
        var sort = order.Resolve("owner")!;

        var ordered = order.Apply(Things.AsQueryable(), sort, after: null, owners).Select(thing => thing.Id).ToList();

        Assert.Equal([Id(3), Id(1), Id(2), Id(4), Id(5), Id(6)], ordered);
        Assert.Throws<InvalidOperationException>(() => order.Apply(Things.AsQueryable(), sort, after: null));
    }

    [Fact]
    public void NoSortIsTheDefaultAndAnUnknownColumnIsRefused()
    {
        Assert.Equal(ListSort.Ascending("name"), Order.Resolve(null));
        Assert.Equal(ListSort.Ascending("name"), Order.Resolve(""));
        Assert.Equal(ListSort.Descending("updated"), Order.Resolve("-updated"));
        Assert.Null(Order.Resolve("colour"));
        Assert.Null(Order.Resolve("-"));
        Assert.Null(Order.Resolve("--name"));
        Assert.Null(Order.Resolve("Name"));
        Assert.False(Order.Allows("colour"));
        Assert.True(Order.Allows(null));
    }

    [Fact]
    public void ACursorFromAnotherSortIsInvalidNotAWrongPage()
    {
        var cursor = Order.Encode(ListSort.Ascending("name"), Things[0], Scope);

        Assert.Equal(ErrorCodes.CursorInvalid, Order.Decode(ListSort.Descending("name"), cursor, Scope).Error?.Code);
        Assert.Equal(ErrorCodes.CursorInvalid, Order.Decode(ListSort.Ascending("note"), cursor, Scope).Error?.Code);
        Assert.Equal(ErrorCodes.CursorInvalid, Order.Decode(ListSort.Ascending("name"), cursor, "things|status=Draft").Error?.Code);
    }

    [Fact]
    public void ACursorWhoseValueDoesNotFitItsColumnIsInvalid()
    {
        // A well-formed cursor in the right scope, but its value cannot be the column's type: never trusted, never thrown.
        var forged = new PageCursor("=not a date", Id(1).ToString("N")).Encode($"{Scope}|sort=-updated");
        var emptyOnARequiredColumn = new PageCursor(string.Empty, Id(1).ToString("N")).Encode($"{Scope}|sort=updated");

        Assert.Equal(ErrorCodes.CursorInvalid, Order.Decode(ListSort.Descending("updated"), forged, Scope).Error?.Code);
        Assert.Equal(ErrorCodes.CursorInvalid, Order.Decode(ListSort.Ascending("updated"), emptyOnARequiredColumn, Scope).Error?.Code);
        Assert.Equal(ErrorCodes.CursorInvalid, Order.Decode(ListSort.Ascending("name"), "garbage", Scope).Error?.Code);
    }

    [Fact]
    public void AnEmptyValueAndEmptyTextStayDistinct()
    {
        var blank = Things[0] with { Note = string.Empty };

        var position = Order.Decode(ListSort.Ascending("note"), Order.Encode(ListSort.Ascending("note"), blank, Scope), Scope);
        var none = Order.Decode(ListSort.Ascending("note"), Order.Encode(ListSort.Ascending("note"), Things[1], Scope), Scope);

        Assert.Equal(string.Empty, position.Value!.Value);
        Assert.Null(none.Value!.Value);
    }

    [Fact]
    public void AColumnOfATypeItCannotPageIsRefusedWhenDeclared()
    {
        var order = new ListOrder<Thing, Thing>(thing => thing.Id, thing => thing.Id, ListSort.Ascending("name"));

        Assert.Throws<NotSupportedException>(() => order.By("id", thing => thing.Id, thing => thing.Id));
        Assert.Throws<ArgumentException>(() => order.By("-name", thing => thing.Name, thing => thing.Name));
    }

    [Theory]
    [InlineData("name", "name", SortDirection.Ascending)]
    [InlineData("-updated", "updated", SortDirection.Descending)]
    public void TheWireFormRoundTrips(string text, string column, SortDirection direction)
    {
        var sort = ListSort.Parse(text);

        Assert.Equal(new ListSort(column, direction), sort);
        Assert.Equal(text, sort!.ToString());
    }

    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    private sealed record Thing(
        Guid Id,
        string Name,
        string? Note,
        PublishStatus Status,
        bool Showing,
        long? PriceCents,
        DateTimeOffset UpdatedAt,
        DateOnly? Opens);
}
