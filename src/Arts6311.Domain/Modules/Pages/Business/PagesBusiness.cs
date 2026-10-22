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

        var (after, cursorError) = DecodePosition(request.Cursor, scope);
        if (cursorError is not null)
        {
            return OperationResult.Failure<CursorPage<PageSummaryServiceModel>>(cursorError);
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

    public async Task<OperationResult<CursorPage<PageDeletedServiceModel>>> ListDeletedAsync(
        PageListViewModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();

        // Its own cursor scope, so a cursor from the status list is not valid here and vice versa. Status is not
        // part of it: the Deleted view shows every deleted page, whatever it was when it went.
        var scope = string.Create(CultureInfo.InvariantCulture, $"pages|deleted|search={search}");

        var (after, cursorError) = DecodePosition(request.Cursor, scope);
        if (cursorError is not null)
        {
            return OperationResult.Failure<CursorPage<PageDeletedServiceModel>>(cursorError);
        }

        var limit = new PageRequest(request.Cursor, request.Limit).EffectiveLimit;
        var rows = await dataLayer.ListDeletedAsync(after, limit + 1, search, cancellationToken);

        string? next = null;
        if (rows.Count > limit)
        {
            rows.RemoveAt(limit);
            var last = rows[^1];
            next = new PageCursor(last.Title, last.Id.ToString("N")).Encode(scope);
        }

        return OperationResult.Success(
            new CursorPage<PageDeletedServiceModel>([.. rows.Select(row => row.ToServiceModel())], next));
    }

    public async Task<OperationResult> DeleteAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        var page = await dataLayer.FindAsync(id, cancellationToken);
        if (page is null)
        {
            return OperationResult.Failure(OperationError.Of(ErrorCodes.NotFound, "There is no such page."));
        }

        if (EntityTag.Verify(page.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure(versionError);
        }

        // From any status, Published included: "this was a mistake" is not a lifecycle step, and a staff member
        // who has to unpublish first learns to unpublish without reading the dialog (ADR 0052). The delete stages
        // the revalidation that takes the page off the site.
        //
        // No cms.in_use check: nothing in the CMS names a page. The types that are named — an artist a published
        // exhibition credits, an instructor a class run names — check it in their own Business.
        return await dataLayer.DeleteAsync(page, actorUserId, cancellationToken) switch
        {
            PageWriteOutcome.Saved => OperationResult.Success(),
            _ => OperationResult.Failure(EntityTag.Conflict(What)),
        };
    }

    public async Task<OperationResult<PageServiceModel>> RestoreAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        var page = await dataLayer.FindDeletedAsync(id, cancellationToken);
        if (page is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(page.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<PageServiceModel>(versionError);
        }

        // The delete freed the address, so another page may be living there now. Refused rather than renamed for
        // the staff member: which of the two keeps the address is their decision, not ours (ADR 0052).
        if (await dataLayer.SlugExistsAsync(page.Slug, page.Id, cancellationToken))
        {
            return OperationResult.Failure<PageServiceModel>(SlugTakenOnRestore(page.Slug));
        }

        var deletedAs = page.Status;

        // Back as a Draft, never straight back onto the site: what publishing requires may no longer hold, and a
        // restore is a decision to look at the page again, not to publish it (ADR 0052).
        page.Status = PublishStatus.Draft;
        page.ScheduledFor = null;

        return await dataLayer.RestoreAsync(page, deletedAs, actorUserId, cancellationToken) switch
        {
            PageWriteOutcome.Saved => OperationResult.Success(ToServiceModel(page)),
            PageWriteOutcome.SlugTaken => OperationResult.Failure<PageServiceModel>(SlugTakenOnRestore(page.Slug)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<SitePageServiceModel>> GetForSiteAsync(string slug, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);

        var page = await dataLayer.FindPublishedAsync(slug, cancellationToken);
        return page is null
            ? OperationResult.Failure<SitePageServiceModel>(OperationError.Of(ErrorCodes.NotFound, "There is no such page."))
            : OperationResult.Success(page);
    }

    public async Task<OperationResult<PreviewRecord<SitePageServiceModel>>> GetForPreviewAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var page = await dataLayer.FindAsync(id, cancellationToken);
        return page is null
            ? OperationResult.Failure<PreviewRecord<SitePageServiceModel>>(OperationError.Of(ErrorCodes.NotFound, "There is no such page."))
            : OperationResult.Success(new PreviewRecord<SitePageServiceModel>(
                new SitePageServiceModel(page.Id, page.Slug, page.Title, page.Body, page.UpdatedAt), page.Status));
    }

    public async Task<IReadOnlyList<SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken) =>
        [.. (await dataLayer.ListPublishedAddressesAsync(cancellationToken))
            .Select(row => new SitemapEntryServiceModel(SitePaths.AddressOf(PagePolicy.SitePathPrefix, row.Slug), row.UpdatedAt))];

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
            return OperationResult.Failure<PageServiceModel>(TransitionInvalid(from, command));
        }

        if (command == PublishCommand.Publish)
        {
            if (PublishRequirementError(page) is { } requirementError)
            {
                return OperationResult.Failure<PageServiceModel>(requirementError);
            }

            page.PublishedAt ??= clock.UtcNow;
            page.ScheduledFor = null;
        }
        else if (command == PublishCommand.Archive)
        {
            // An archived page is never published by the scheduled publisher, so a time left on it would only
            // mislead the staff reading it.
            page.ScheduledFor = null;
        }

        page.Status = to;

        return await dataLayer.TransitionAsync(page, command, from, actorUserId, cancellationToken) switch
        {
            PageWriteOutcome.Saved => OperationResult.Success(ToServiceModel(page)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<PageServiceModel>> ScheduleAsync(
        Guid id, DateTimeOffset scheduledFor, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
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
        if (PublishLifecycle.Next(from, PublishCommand.Schedule) is not { } to)
        {
            return OperationResult.Failure<PageServiceModel>(TransitionInvalid(from, PublishCommand.Schedule));
        }

        // A time already past would be published by the next pass, a minute later, which is not what "schedule"
        // means: staff who want it out now have the publish command for that.
        if (scheduledFor <= clock.UtcNow)
        {
            return OperationResult.Failure<PageServiceModel>(OperationError.Validation(
                "Choose a time in the future.",
                [("scheduledFor", "This time has passed. Publish the page now instead, or pick a later time.")]));
        }

        // Checked now as well as at publish, so staff hear about an empty or placeholder body while they are
        // looking at the screen rather than at midnight, when only the audit trail is watching (ADR 0036).
        if (PublishRequirementError(page) is { } requirementError)
        {
            return OperationResult.Failure<PageServiceModel>(requirementError);
        }

        page.ScheduledFor = scheduledFor;
        page.Status = to;

        return await dataLayer.TransitionAsync(page, PublishCommand.Schedule, from, actorUserId, cancellationToken) switch
        {
            PageWriteOutcome.Saved => OperationResult.Success(ToServiceModel(page)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<PageServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
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
        if (PublishLifecycle.Next(from, PublishCommand.Unschedule) is not { } to)
        {
            return OperationResult.Failure<PageServiceModel>(TransitionInvalid(from, PublishCommand.Unschedule));
        }

        page.ScheduledFor = null;
        page.Status = to;

        return await dataLayer.TransitionAsync(page, PublishCommand.Unschedule, from, actorUserId, cancellationToken) switch
        {
            PageWriteOutcome.Saved => OperationResult.Success(ToServiceModel(page)),
            _ => Conflict(),
        };
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        dataLayer.ListDueAsync(asOf, take, cancellationToken);

    public async Task<ScheduledPublishOutcome> PublishScheduledAsync(
        Guid id, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var page = await dataLayer.FindAsync(id, cancellationToken);

        // Read again inside this scope, because the due list is a snapshot: staff may have published, rescheduled
        // or archived the page since, and another Worker may have taken it.
        if (page is null || page.Status != PublishStatus.Scheduled || page.ScheduledFor is null || page.ScheduledFor > asOf)
        {
            return ScheduledPublishOutcome.Skipped;
        }

        var from = page.Status;
        if (PublishRequirementError(page) is { } requirementError)
        {
            // Back to Draft rather than left Scheduled: a record whose time has passed would otherwise be retried
            // every minute for ever, and nobody would be told (ADR 0036).
            page.ScheduledFor = null;
            page.Status = PublishStatus.Draft;

            return await dataLayer.AbandonScheduleAsync(page, from, requirementError.Message, cancellationToken) switch
            {
                PageWriteOutcome.Saved => ScheduledPublishOutcome.Unscheduled,
                _ => ScheduledPublishOutcome.Lost,
            };
        }

        page.PublishedAt ??= clock.UtcNow;
        page.ScheduledFor = null;
        page.Status = PublishStatus.Published;

        return await dataLayer.TransitionAsync(
            page, PublishCommand.Publish, from, ScheduledPublishPolicy.Actor, cancellationToken) switch
        {
            PageWriteOutcome.Saved => ScheduledPublishOutcome.Published,

            // The row version moved under this save: another Worker published it, or staff changed it. Either way
            // it is theirs now.
            _ => ScheduledPublishOutcome.Lost,
        };
    }

    /// <summary>
    /// content.md: what the site needs is checked at publish, and again at schedule. The pictures were checked when
    /// the body was saved, and their <c>Restrict</c> references have kept them in the library since, so only the
    /// text itself is in question here.
    /// </summary>
    private OperationError? PublishRequirementError(Page page)
    {
        if (string.IsNullOrEmpty(page.Body))
        {
            return OperationError.Validation(
                "This page isn't ready to publish yet.",
                [("body", "Write the page first: a published page with no text would be empty on the site.")]);
        }

        // A legal placeholder published by a slip would tell visitors the policy "has not been written yet".
        return IsPlaceholder(page.Body)
            ? OperationError.Validation(
                "This page still has its placeholder text.",
                [("body", "Replace the placeholder with the real text, then publish.")])
            : null;
    }

    private static OperationError TransitionInvalid(PublishStatus from, PublishCommand command) => OperationError.Of(
        ContentErrorCodes.StatusTransitionInvalid,
        $"This page is {from.ToString().ToLowerInvariant()}, so “{command.Verb()}” doesn't apply. Reload it to see its current status.");

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

    /// <summary>
    /// Whether <paramref name="body"/> is still a seeded placeholder, untouched. Both sides are compared as the
    /// sanitizer has them now, so a placeholder seeded under a wider allow-list (its <c>mailto:</c> link, before
    /// ADR 0045) still counts.
    /// </summary>
    private bool IsPlaceholder(string body)
    {
        var current = sanitizer.Sanitize(body).Html;
        return PagePlaceholderSeeder.Placeholders.Any(placeholder =>
            string.Equals(sanitizer.Sanitize(placeholder.Body).Html, current, StringComparison.Ordinal));
    }

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

    /// <summary>Where a keyset page resumes, or why the cursor can't be read. Shared by the list and the Deleted view.</summary>
    private static (PageListPosition? After, OperationError? Error) DecodePosition(string? cursor, string scope)
    {
        if (cursor is null)
        {
            return (null, null);
        }

        var decoded = PageCursor.Decode(cursor, scope);
        return !decoded.Succeeded || !Guid.TryParseExact(decoded.Value!.TieBreaker, "N", out var afterId)
            ? (null, OperationError.Of(ErrorCodes.CursorInvalid, "The cursor is not valid for this list."))
            : (new PageListPosition(decoded.Value.SortValue, afterId), null);
    }

    private static OperationError SlugTakenOnRestore(string slug) => new(
        ContentErrorCodes.SlugTaken,
        "Another page has taken this one's web address.",
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["slug"] = [$"“{slug}” was free while this page was deleted, and another page took it. Give that page another address, then restore this one."],
        });

    private static OperationError SlugTaken(string slug) => new(
        ContentErrorCodes.SlugTaken,
        "Another page already has this web address.",
        new Dictionary<string, string[]>(StringComparer.Ordinal) { ["slug"] = [$"“{slug}” is taken. Choose another."] });

}
