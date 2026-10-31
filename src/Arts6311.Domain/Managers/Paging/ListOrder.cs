using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Arts6311.Domain.Managers.Results;

namespace Arts6311.Domain.Managers.Paging;

/// <summary>
/// The columns one CMS list can be sorted by, and the keyset paging that goes with each (ADR 0062). A module
/// declares it once, beside its repository; Business resolves the requested <see cref="ListSort"/> and turns cursors
/// into <see cref="ListPosition"/>s, and the repository hands its filtered query to <see cref="Apply"/>.
/// </summary>
/// <typeparam name="TEntity">What the repository queries.</typeparam>
/// <typeparam name="TRow">What the repository returns, which the next cursor is read from.</typeparam>
/// <remarks>
/// <para>
/// Every order is the column, then the id to break ties (ascending unless the list's own order always broke them the
/// other way, which it keeps, so no list's order moved); the WHERE resumes in exactly the ORDER BY's terms, so a page boundary neither skips nor repeats a row whichever column the list is on. A column that can be
/// empty sorts its empty rows last in both directions, as the leading ORDER BY term, and the keyset treats an
/// empty value as one past every other (the shop's price orders, ADR 0042, generalised).
/// </para>
/// <para>
/// The sort is part of the cursor's scope, so a cursor from one order is <see cref="ErrorCodes.CursorInvalid"/> in
/// another rather than a wrong page. The value it carries is typed by the column: text as written, a status as its
/// number (so the workflow order, not the alphabet), a date as its round-trip form.
/// </para>
/// </remarks>
public sealed class ListOrder<TEntity, TRow>
{
    // Inside the cursor's sort value, which is a plain string: a value is written after this marker, and no value
    // (an empty column) is the empty string, so "" and none stay distinct.
    private const char ValueMarker = '=';

    private readonly Dictionary<string, Column> columns = new(StringComparer.Ordinal);
    private readonly Expression<Func<TEntity, Guid>> entityId;
    private readonly Func<TRow, Guid> rowId;
    private readonly SortDirection tieBreak;

    /// <param name="entityId">The entity's key, the tie-breaker.</param>
    /// <param name="rowId">The same key on a returned row.</param>
    /// <param name="defaultSort">The order when a request names none; its column must be added with <c>By</c>.</param>
    /// <param name="tieBreak">Which way the id breaks ties, in every order: the way the list always broke them.</param>
    public ListOrder(
        Expression<Func<TEntity, Guid>> entityId,
        Func<TRow, Guid> rowId,
        ListSort defaultSort,
        SortDirection tieBreak = SortDirection.Ascending)
    {
        ArgumentNullException.ThrowIfNull(entityId);
        ArgumentNullException.ThrowIfNull(rowId);
        ArgumentNullException.ThrowIfNull(defaultSort);

        this.entityId = entityId;
        this.rowId = rowId;
        this.tieBreak = tieBreak;
        Default = defaultSort;
    }

    public ListSort Default { get; }

    /// <summary>The column names a request may sort by, for a validation message.</summary>
    public IReadOnlyCollection<string> Columns => columns.Keys;

    /// <summary>Adds a column: its key on the entity, for the query, and the same key on a returned row.</summary>
    /// <remarks>
    /// Text, a flag, a status (any enum), a whole number, a decimal or a date, each optionally nullable. Anything else
    /// throws here, when the module's order is first built, rather than when a member of staff clicks the header.
    /// </remarks>
    public ListOrder<TEntity, TRow> By<TKey>(
        string column, Expression<Func<TEntity, TKey>> entityKey, Func<TRow, TKey> rowKey)
    {
        ArgumentNullException.ThrowIfNull(entityKey);
        ArgumentNullException.ThrowIfNull(rowKey);

        columns.Add(Named(column), new Column<TKey>(entityKey, rowKey, contextType: null));
        return this;
    }

    /// <summary>
    /// Adds a column whose key needs something only the query knows, handed to <see cref="Apply"/> as its
    /// <c>context</c>: the moment the list is read, or another module's table that a foreign key already reaches
    /// (whose name an artwork's artist column orders by). A queryable in it is inlined into the query; any other value
    /// is sent as a parameter.
    /// </summary>
    public ListOrder<TEntity, TRow> By<TContext, TKey>(
        string column, Expression<Func<TEntity, TContext, TKey>> entityKey, Func<TRow, TKey> rowKey)
    {
        ArgumentNullException.ThrowIfNull(entityKey);
        ArgumentNullException.ThrowIfNull(rowKey);

        columns.Add(Named(column), new Column<TKey>(entityKey, rowKey, typeof(TContext)));
        return this;
    }

    private static string Named(string column)
    {
        ArgumentException.ThrowIfNullOrEmpty(column);
        return column[0] == '-'
            ? throw new ArgumentException("A column name cannot start with '-': that is how descending is written.", nameof(column))
            : column;
    }

    /// <summary>
    /// The sort a request named, or <see cref="Default"/> when it named none; <c>null</c> when it names a column this
    /// list does not sort by.
    /// </summary>
    public ListSort? Resolve(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Default;
        }

        var sort = ListSort.Parse(text);
        return sort is not null && columns.ContainsKey(sort.Column) ? sort : null;
    }

    /// <summary>Whether <see cref="Resolve"/> would accept <paramref name="text"/>: the validator's rule.</summary>
    public bool Allows(string? text) => Resolve(text) is not null;

    /// <summary>The cursor that resumes after <paramref name="last"/>, under this sort and the list's filters.</summary>
    public string Encode(ListSort sort, TRow last, string scope)
    {
        var value = ColumnOf(sort).Format(last);
        return new PageCursor(value is null ? string.Empty : ValueMarker + value, rowId(last).ToString("N"))
            .Encode(ScopeOf(sort, scope));
    }

    /// <summary>
    /// Where <paramref name="cursor"/> resumes, or <see cref="ErrorCodes.CursorInvalid"/> when it came from another
    /// list, other filters, another sort, or was never a cursor.
    /// </summary>
    public OperationResult<ListPosition> Decode(ListSort sort, string cursor, string scope)
    {
        var invalid = OperationResult.Failure<ListPosition>(
            OperationError.Of(ErrorCodes.CursorInvalid, "The cursor is not valid for this list."));

        var decoded = PageCursor.Decode(cursor, ScopeOf(sort, scope));
        if (!decoded.Succeeded || !Guid.TryParseExact(decoded.Value!.TieBreaker, "N", out var id))
        {
            return invalid;
        }

        var text = decoded.Value.SortValue;
        if (text.Length > 0 && text[0] != ValueMarker)
        {
            return invalid;
        }

        return ColumnOf(sort).TryParse(text.Length == 0 ? null : text[1..], out var value)
            ? OperationResult.Success(new ListPosition(value, id))
            : invalid;
    }

    /// <summary>
    /// <paramref name="query"/> in this sort's order, starting after <paramref name="after"/> when there is one. The
    /// caller adds <c>Take</c> and its projection.
    /// </summary>
    /// <param name="context">What a column added with a context needs; ignored by the others.</param>
    public IQueryable<TEntity> Apply(IQueryable<TEntity> query, ListSort sort, ListPosition? after, object? context = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ColumnOf(sort).Apply(query, sort.Direction, after, entityId, tieBreak, context);
    }

    private Column ColumnOf(ListSort sort)
    {
        ArgumentNullException.ThrowIfNull(sort);
        return columns.TryGetValue(sort.Column, out var column)
            ? column
            : throw new ArgumentOutOfRangeException(nameof(sort), sort.Column, "This list does not sort by that column.");
    }

    private static string ScopeOf(ListSort sort, string scope) => $"{scope}|sort={sort}";

    private abstract class Column
    {
        public abstract string? Format(TRow row);

        public abstract bool TryParse(string? text, out object? value);

        public abstract IQueryable<TEntity> Apply(
            IQueryable<TEntity> query,
            SortDirection direction,
            ListPosition? after,
            Expression<Func<TEntity, Guid>> id,
            SortDirection tieBreak,
            object? context);
    }

    private sealed class Column<TKey> : Column
    {
        private static readonly MethodInfo StringCompare =
            typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])!;

        private static readonly MethodInfo GuidCompareTo =
            typeof(Guid).GetMethod(nameof(Guid.CompareTo), [typeof(Guid)])!;

        private readonly LambdaExpression entityKey;
        private readonly Func<TRow, TKey> rowKey;
        private readonly Type? contextType;
        private readonly Type valueType;
        private readonly bool nullable;

        /// <param name="entityKey">The entity alone, or the entity and a <paramref name="contextType"/>.</param>
        public Column(LambdaExpression entityKey, Func<TRow, TKey> rowKey, Type? contextType)
        {
            this.entityKey = entityKey;
            this.rowKey = rowKey;
            this.contextType = contextType;

            var underlying = Nullable.GetUnderlyingType(typeof(TKey));
            valueType = underlying ?? typeof(TKey);
            nullable = underlying is not null || !typeof(TKey).IsValueType;

            if (!Supported(valueType))
            {
                throw new NotSupportedException($"A list cannot be sorted by a {typeof(TKey).Name}.");
            }
        }

        public override string? Format(TRow row) => rowKey(row) switch
        {
            null => null,
            string text => text,
            bool flag => flag ? "1" : "0",
            Enum status => Convert.ToInt64(status, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            DateTimeOffset at => at.ToString("O", CultureInfo.InvariantCulture),
            DateTime at => at.ToString("O", CultureInfo.InvariantCulture),
            DateOnly day => day.ToString("O", CultureInfo.InvariantCulture),
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            var other => throw new NotSupportedException($"A list cannot be sorted by a {other.GetType().Name}."),
        };

        public override bool TryParse(string? text, out object? value)
        {
            value = null;
            if (text is null)
            {
                return nullable;
            }

            const NumberStyles Whole = NumberStyles.AllowLeadingSign;
            const DateTimeStyles RoundTrip = DateTimeStyles.RoundtripKind;
            var invariant = CultureInfo.InvariantCulture;

            if (valueType == typeof(string))
            {
                value = text;
            }
            else if (valueType == typeof(bool))
            {
                value = text switch { "1" => true, "0" => false, _ => null };
            }
            else if (valueType.IsEnum)
            {
                value = long.TryParse(text, Whole, invariant, out var number) ? Enum.ToObject(valueType, number) : null;
            }
            else if (valueType == typeof(int))
            {
                value = int.TryParse(text, Whole, invariant, out var number) ? number : null;
            }
            else if (valueType == typeof(long))
            {
                value = long.TryParse(text, Whole, invariant, out var number) ? number : null;
            }
            else if (valueType == typeof(decimal))
            {
                value = decimal.TryParse(text, NumberStyles.Number, invariant, out var number) ? number : null;
            }
            else if (valueType == typeof(DateTimeOffset))
            {
                value = DateTimeOffset.TryParseExact(text, "O", invariant, RoundTrip, out var at) ? at : null;
            }
            else if (valueType == typeof(DateTime))
            {
                value = DateTime.TryParseExact(text, "O", invariant, RoundTrip, out var at) ? at : null;
            }
            else if (valueType == typeof(DateOnly))
            {
                value = DateOnly.TryParseExact(text, "O", invariant, DateTimeStyles.None, out var day) ? day : null;
            }

            return value is not null;
        }

        public override IQueryable<TEntity> Apply(
            IQueryable<TEntity> query,
            SortDirection direction,
            ListPosition? after,
            Expression<Func<TEntity, Guid>> id,
            SortDirection tieBreak,
            object? context)
        {
            var entityKey = Bind(context);
            var row = entityKey.Parameters[0];
            var key = entityKey.Body;
            var rowId = new ParameterSwap(id.Parameters[0], row).Visit(id.Body);
            var isNull = nullable ? Expression.Equal(key, Expression.Constant(null, typeof(TKey))) : null;

            if (after is not null)
            {
                // Held in a closure rather than written as constants, so EF sends them as parameters.
                var tieBreaker = Expression.Property(Expression.Constant(new Held<Guid>(after.Id)), nameof(Held<Guid>.Value));
                var comparedId = Expression.Call(rowId, GuidCompareTo, tieBreaker);
                var laterId = tieBreak == SortDirection.Ascending
                    ? Expression.GreaterThan(comparedId, Expression.Constant(0))
                    : Expression.LessThan(comparedId, Expression.Constant(0));

                Expression resume;
                if (after.Value is null)
                {
                    // Already among the empty ones, which come last: only later ids of those remain.
                    resume = isNull is null ? Expression.Constant(false) : Expression.AndAlso(isNull, laterId);
                }
                else
                {
                    var value = Expression.Property(Expression.Constant(new Held<TKey>((TKey)after.Value)), nameof(Held<TKey>.Value));
                    resume = Expression.OrElse(Beyond(key, value, (TKey)after.Value, direction), Expression.AndAlso(Expression.Equal(key, value), laterId));
                    if (isNull is not null)
                    {
                        resume = Expression.OrElse(isNull, resume);
                    }
                }

                query = query.Where(Expression.Lambda<Func<TEntity, bool>>(resume, row));
            }

            IOrderedQueryable<TEntity> ordered;
            if (isNull is not null)
            {
                ordered = Order(query, Expression.Lambda(isNull, row), nameof(Queryable.OrderBy));
                ordered = Order(ordered, entityKey, direction == SortDirection.Ascending ? nameof(Queryable.ThenBy) : nameof(Queryable.ThenByDescending));
            }
            else
            {
                ordered = Order(query, entityKey, direction == SortDirection.Ascending ? nameof(Queryable.OrderBy) : nameof(Queryable.OrderByDescending));
            }

            return Order(
                ordered,
                Expression.Lambda(rowId, row),
                tieBreak == SortDirection.Ascending ? nameof(Queryable.ThenBy) : nameof(Queryable.ThenByDescending));
        }

        /// <summary>The key on the entity alone, the query's context written into it where the column needs one.</summary>
        private Expression<Func<TEntity, TKey>> Bind(object? context)
        {
            if (contextType is null)
            {
                return (Expression<Func<TEntity, TKey>>)entityKey;
            }

            if (context is null || !contextType.IsInstanceOfType(context))
            {
                throw new InvalidOperationException($"This column is ordered with a {contextType.Name}, which the query did not pass.");
            }

            // Read from a closure, as a captured variable would be, so EF evaluates it: a queryable becomes part of the
            // query and a value a parameter.
            var held = Expression.Convert(
                Expression.Property(Expression.Constant(new Held<object>(context)), nameof(Held<object>.Value)), contextType);
            var body = new ParameterSwap(entityKey.Parameters[1], held).Visit(entityKey.Body);
            return Expression.Lambda<Func<TEntity, TKey>>(body, entityKey.Parameters[0]);
        }

        /// <summary>The key strictly past <paramref name="value"/> in this direction: <c>&gt;</c> ascending, <c>&lt;</c> descending.</summary>
        private Expression Beyond(Expression key, Expression value, TKey known, SortDirection direction)
        {
            var ascending = direction == SortDirection.Ascending;

            if (valueType == typeof(string))
            {
                // Translated to SQL's ">" under the column's own collation — the same order as the ORDER BY.
                var compared = Expression.Call(StringCompare, key, value);
                var zero = Expression.Constant(0);
                return ascending ? Expression.GreaterThan(compared, zero) : Expression.LessThan(compared, zero);
            }

            if (valueType == typeof(bool))
            {
                // No ">" between flags in an expression tree, and only two values: past "no" is "yes" and the other way about.
                var flag = (bool)(object)known!;
                return ascending == flag
                    ? Expression.Constant(false)
                    : Expression.Equal(key, Expression.Constant(ascending, typeof(TKey)));
            }

            if (valueType.IsEnum)
            {
                // Compared as the stored number, which is the order the enum declares (the workflow, for a status).
                var number = Enum.GetUnderlyingType(valueType);
                var asNumber = nullable ? typeof(Nullable<>).MakeGenericType(number) : number;
                key = Expression.Convert(key, asNumber);
                value = Expression.Convert(value, asNumber);
            }

            return ascending ? Expression.GreaterThan(key, value) : Expression.LessThan(key, value);
        }

        private static bool Supported(Type type) =>
            type == typeof(string) || type == typeof(bool) || type.IsEnum
            || type == typeof(int) || type == typeof(long) || type == typeof(decimal)
            || type == typeof(DateTimeOffset) || type == typeof(DateTime) || type == typeof(DateOnly);

        private static IOrderedQueryable<TEntity> Order(IQueryable<TEntity> query, LambdaExpression key, string method) =>
            (IOrderedQueryable<TEntity>)query.Provider.CreateQuery<TEntity>(Expression.Call(
                typeof(Queryable), method, [typeof(TEntity), key.ReturnType], query.Expression, Expression.Quote(key)));
    }

    private sealed class Held<T>(T value)
    {
        public T Value { get; } = value;
    }

    /// <summary>Rewrites one parameter as another expression: the id selector onto the key's row, a context onto its value.</summary>
    private sealed class ParameterSwap(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }
}
