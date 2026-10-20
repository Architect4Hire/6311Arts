using System.Globalization;
using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Html;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Pages.Data;
using Arts6311.Domain.Modules.Pages.Data.Entities;
using Arts6311.Domain.Modules.Pages.Managers;
using Arts6311.Domain.Modules.Pages.Managers.ServiceModels;
using Arts6311.Domain.Modules.Pages.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Pages.Business;

/// <summary>
/// The page rules: the body is sanitized on write and only the sanitizer's output is kept (content.md, ADR 0034);
/// every picture in it is a library original with its own alt text; slugs are unique and locked once published;
/// status changes only along the lifecycle; and every write is checked against the row version the editor read.
/// </summary>
internal sealed class PagesBusiness(IPagesDataLayer dataLayer, IHtmlSanitizer sanitizer, IClock clock) : IPagesBusiness
{
    private const string What = "this page";

    public async Task<OperationResult<CursorPage<PageSummaryServiceModel>>> ListAsync(
        PageListViewModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        var scope = string.Create(CultureInfo.InvariantCulture, $"pages|status={request.Status}|search={search}");

        PageListPosition? after = null;
        if (request.Cursor is not null)
        {
            var decoded = PageCursor.Decode(request.Cursor, scope);
            if (!decoded.Succeeded || !Guid.TryParseExact(decoded.Value!.TieBreaker, "N", out var afterId))
            {
                return OperationResult.Failure<CursorPage<PageSummaryServiceModel>>(
                    OperationError.Of(ErrorCodes.CursorInvalid, "The cursor is not valid for this list."));
            }

            after = new PageListPosition(decoded.Value.SortValue, afterId);
        }

        var limit = new PageRequest(request.Cursor, request.Limit).EffectiveLimit;
        var rows = await dataLayer.ListAsync(after, limit + 1, request.Status, search, cancellationToken);

        string? next = null;
        if (rows.Count > limit)
        {
            rows.RemoveAt(limit);
            var last = rows[^1];
            next = new PageCursor(last.Title, last.Id.ToString("N")).Encode(scope);
        }

        return OperationResult.Success(new CursorPage<PageSummaryServiceModel>(rows, next));
    }

    public async Task<OperationResult<PageServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var page = await dataLayer.FindAsync(id, cancellationToken);
        return page is null ? NotFound() : OperationResult.Success(ToServiceModel(page));
    }

    public async Task<OperationResult<PageServiceModel>> CreateAsync(
        PageCreateViewModel create, PageImageCheck images, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(create);

        var title = create.Title.Trim();
        var slug = create.Slug ?? Slugs.FromName(title);

        var (body, bodyError) = PrepareBody(create.Body, images);
        if (bodyError is not null)
        {
            return OperationResult.Failure<PageServiceModel>(bodyError);
        }

        if (PagePolicy.ReservedSlugs.Contains(slug))
        {
            return OperationResult.Failure<PageServiceModel>(SlugReserved(slug));
        }

        if (await dataLayer.SlugExistsAsync(slug, exceptId: null, cancellationToken))
        {
            return OperationResult.Failure<PageServiceModel>(SlugTaken(slug));
        }

        var page = new Page
        {
            Id = Guid.CreateVersion7(clock.UtcNow),
            Title = title,
            Slug = slug,
            Status = PublishStatus.Draft,
        };
        SetBody(page, body);

        var summary = AuditSummary("Created as Draft", [], body);
        return await dataLayer.CreateAsync(page, actorUserId, summary, cancellationToken) switch
        {
            PageWriteOutcome.Saved => OperationResult.Success(ToServiceModel(page)),
            PageWriteOutcome.SlugTaken => OperationResult.Failure<PageServiceModel>(SlugTaken(slug)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<PageServiceModel>> UpdateAsync(
        Guid id,
        PageUpdateViewModel update,
        string? ifMatch,
        PageImageCheck? images,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        var page = await dataLayer.FindAsync(id, cancellationToken);
        if (page is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(page.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<PageServiceModel>(versionError);
        }

        var changes = new List<string>();

        if (update.Title is not null && update.Title.Trim() is var title && !string.Equals(title, page.Title, StringComparison.Ordinal))
        {
            page.Title = title;
            changes.Add("title");
        }

        PublishedPathChange? pathChange = null;
        if (update.Slug is not null && !string.Equals(update.Slug, page.Slug, StringComparison.Ordinal))
        {
            if (PagePolicy.ReservedSlugs.Contains(update.Slug))
            {
                return OperationResult.Failure<PageServiceModel>(SlugReserved(update.Slug));
            }

            if (await dataLayer.SlugExistsAsync(update.Slug, page.Id, cancellationToken))
            {
                return OperationResult.Failure<PageServiceModel>(SlugTaken(update.Slug));
            }

            // A slug that has ever been public is someone's bookmark: the old address is redirected to the new one,
            // staged with this save (content.md, ADR 0035).
            pathChange = PublishedPathChange.For(PagePolicy.SitePathPrefix, page.Slug, update.Slug, page.PublishedAt);
            page.Slug = update.Slug;
            changes.Add(pathChange is null ? "slug" : $"slug (redirecting {pathChange.FromPath})");
        }

        SanitizedHtml? body = null;
        if (update.Body is not null)
        {
            (body, var bodyError) = PrepareBody(update.Body, images ?? PageImageCheck.None);
            if (bodyError is not null)
            {
                return OperationResult.Failure<PageServiceModel>(bodyError);
            }

            var stored = body!.IsEmpty ? null : body.Html;
            if (!string.Equals(stored, page.Body, StringComparison.Ordinal))
            {
                // content.md: what the site needs is checked at publish, and published content keeps it — archived
                // content too, since it was public and stays on record as it was.
                if (stored is null && page.Status != PublishStatus.Draft)
                {
                    return OperationResult.Failure<PageServiceModel>(OperationError.Validation(
                        "This page is published, so it keeps a body.",
                        [("body", "Edit the text rather than clearing it, or unpublish the page first.")]));
                }

                SetBody(page, body);
                changes.Add(stored is null ? "body cleared" : "body");
            }
        }

        if (changes.Count == 0)
        {
            return OperationResult.Success(ToServiceModel(page));
        }

        var summary = AuditSummary("Updated", changes, body);
        return await dataLayer.UpdateAsync(page, actorUserId, summary, pathChange, cancellationToken) switch
        {
            PageWriteOutcome.Saved => OperationResult.Success(ToServiceModel(page)),
            PageWriteOutcome.SlugTaken => OperationResult.Failure<PageServiceModel>(SlugTaken(page.Slug)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<PageServiceModel>> TransitionAsync(
        Guid id, PublishCommand command, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        var page = await dataLayer.FindAsync(id, cancellationToken);
        if (page is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(page.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<PageServiceModel>(versionError);
        }

        var from = page.Status;
        if (PublishLifecycle.Next(from, command) is not { } to)
        {
            return OperationResult.Failure<PageServiceModel>(OperationError.Of(
                ContentErrorCodes.StatusTransitionInvalid,
                $"This page is {from.ToString().ToLowerInvariant()}, so “{command.Verb()}” doesn't apply. Reload it to see its current status."));
        }

        if (command == PublishCommand.Publish)
        {
            // content.md: what the site needs is checked at publish. The pictures were checked when the body was saved,
            // and their Restrict references have kept them in the library since.
            if (string.IsNullOrEmpty(page.Body))
            {
                return OperationResult.Failure<PageServiceModel>(OperationError.Validation(
                    "This page isn't ready to publish yet.",
                    [("body", "Write the page first: a published page with no text would be empty on the site.")]));
            }

            // A legal placeholder published by a slip would tell visitors the policy "has not been written yet".
            if (IsPlaceholder(page.Body))
            {
                return OperationResult.Failure<PageServiceModel>(OperationError.Validation(
                    "This page still has its placeholder text.",
                    [("body", "Replace the placeholder with the real text, then publish.")]));
            }

            page.PublishedAt ??= clock.UtcNow;
            page.ScheduledFor = null;
        }

        page.Status = to;

        return await dataLayer.TransitionAsync(page, command, from, actorUserId, cancellationToken) switch
        {
            PageWriteOutcome.Saved => OperationResult.Success(ToServiceModel(page)),
            _ => Conflict(),
        };
    }

    public async Task<int> SeedAsync(IReadOnlyList<PagePlaceholder> placeholders, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(placeholders);

        var now = clock.UtcNow;
        var taken = await dataLayer.GetSlugsAsync(cancellationToken);
        var pages = new List<Page>();

        foreach (var placeholder in placeholders)
        {
            if (!taken.Add(placeholder.Slug))
            {
                continue;
            }

            var page = new Page
            {
                Id = Guid.CreateVersion7(now),
                Title = placeholder.Title,
                Slug = placeholder.Slug,
                Status = PublishStatus.Draft,
            };

            // Placeholders go through the same sanitizer as anything staff write: no write path skips it.
            SetBody(page, sanitizer.Sanitize(placeholder.Body));
            pages.Add(page);
        }

        return pages.Count == 0 ? 0 : await dataLayer.AddSeedAsync(pages, actor, cancellationToken);
    }

    /// <summary>
    /// The body as it would be stored, or why it can't be: too long once sanitized, a picture without alt text, or a
    /// picture Media doesn't have. The facade asked Media about the same sanitized body; an answer that doesn't cover a
    /// picture counts as Media not having it.
    /// </summary>
    private (SanitizedHtml? Body, OperationError? Error) PrepareBody(string? raw, PageImageCheck images)
    {
        var body = sanitizer.Sanitize(raw);

        if (body.Refused is { } refusal)
        {
            return (null, OperationError.Validation(
                "The page's text couldn't be read.",
                [("body", $"{refusal} Paste it as plain text, or simplify its formatting, and try again.")]));
        }

        if (body.Html.Length > PagePolicy.BodyMaxLength)
        {
            return (null, OperationError.Validation(
                "The page is too long.",
                [("body", string.Create(CultureInfo.InvariantCulture, $"Keep the page under {PagePolicy.BodyMaxLength:N0} characters of formatted text. Split it into two pages if it needs to be longer."))]));
        }

        var problems = new List<(string Field, string Error)>();

        var undescribed = body.Images.Count(image => image.AltText is null);
        if (undescribed > 0)
        {
            problems.Add(("body", undescribed == 1
                ? "A picture has no alt text. Describe it for people who can't see it."
                : string.Create(CultureInfo.InvariantCulture, $"{undescribed} pictures have no alt text. Describe each for people who can't see them.")));
        }

        // Not asked about counts as not vouched for: the facade's answer must cover the very body being stored.
        if (body.MediaAssetIds.Any(id => !images.Asked.Contains(id) || images.Missing.Contains(id)))
        {
            problems.Add(("body", "A picture isn't in the media library, or couldn't be checked. Remove it or choose another, then save again."));
        }

        return problems.Count == 0
            ? (body, null)
            : (null, OperationError.Validation("The page's text needs attention.", problems));
    }

    /// <summary>Whether <paramref name="body"/> is still a seeded placeholder, untouched.</summary>
    private bool IsPlaceholder(string body) =>
        PagePlaceholderSeeder.Placeholders.Any(placeholder =>
            string.Equals(sanitizer.Sanitize(placeholder.Body).Html, body, StringComparison.Ordinal));

    /// <summary>Stores the sanitized body and makes the image references exactly the pictures it shows.</summary>
    private static void SetBody(Page page, SanitizedHtml? body)
    {
        page.Body = body is null || body.IsEmpty ? null : body.Html;

        var wanted = body?.MediaAssetIds ?? [];
        page.Images.RemoveAll(reference => !wanted.Contains(reference.MediaAssetId));
        foreach (var assetId in wanted.Where(assetId => page.Images.All(reference => reference.MediaAssetId != assetId)))
        {
            page.Images.Add(new PageMediaReference { PageId = page.Id, MediaAssetId = assetId });
        }
    }

    /// <summary>The audit line, with what the sanitizer removed (in words) when it removed anything.</summary>
    private static string AuditSummary(string action, List<string> changes, SanitizedHtml? body)
    {
        var summary = changes.Count == 0 ? $"{action}." : $"{action}: {string.Join(", ", changes)}.";
        if (body is { Removed.Count: > 0 })
        {
            summary += $" Sanitizer removed: {string.Join("; ", body.Removed)}.";
        }

        return summary.Length <= AuditPolicy.SummaryMaxLength ? summary : summary[..(AuditPolicy.SummaryMaxLength - 1)] + "…";
    }

    private static PageServiceModel ToServiceModel(Page page) => new(
        page.Id,
        page.Title,
        page.Slug,
        page.Body,
        [.. page.Images.Select(reference => reference.MediaAssetId)],
        page.Status,
        page.PublishedAt,
        page.ScheduledFor,
        SlugLocked: page.PublishedAt is not null,
        page.LegacyWordPressId,
        page.CreatedAt,
        page.UpdatedAt,
        page.UpdatedBy,
        EntityTag.Format(page.RowVersion));

    private static OperationResult<PageServiceModel> NotFound() =>
        OperationResult.Failure<PageServiceModel>(OperationError.Of(ErrorCodes.NotFound, "There is no such page."));

    private static OperationResult<PageServiceModel> Conflict() =>
        OperationResult.Failure<PageServiceModel>(EntityTag.Conflict(What));

    private static OperationError SlugReserved(string slug) => OperationError.Validation(
        "This web address belongs to the site itself.",
        [("slug", $"6311arts.com/{slug} is already part of the site. Choose another address for this page.")]);

    private static OperationError SlugTaken(string slug) => new(
        ContentErrorCodes.SlugTaken,
        "Another page already has this web address.",
        new Dictionary<string, string[]>(StringComparer.Ordinal) { ["slug"] = [$"“{slug}” is taken. Choose another."] });

}
