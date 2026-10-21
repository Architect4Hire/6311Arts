using System.Globalization;
using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Html;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Shop.Data;
using Arts6311.Domain.Modules.Shop.Data.Entities;
using Arts6311.Domain.Modules.Shop.Managers;
using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;
using Arts6311.Domain.Modules.Shop.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Shop.Business;

/// <summary>
/// The shop's rules: the description is sanitized on write and only the sanitizer's output is kept (ADR 0034);
/// every picture in it is a library original with its own alt text; slugs are unique and a published one's change
/// owes a redirect; the SKU is one no other record in the CMS holds (ADR 0037); a one-of-a-kind piece is always a
/// count of one; and Square's record of price, stock and sold-out is never written from here (ADR 0032).
/// </summary>
internal sealed class ShopBusiness(IShopDataLayer dataLayer, IHtmlSanitizer sanitizer, IClock clock) : IShopBusiness
{
    private const string What = "this item";

    public async Task<OperationResult<CursorPage<ShopItemListRow>>> ListAsync(
        ShopItemListViewModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        var category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category;
        var scope = string.Create(
            CultureInfo.InvariantCulture, $"shop|status={request.Status}|category={category}|search={search}");

        ShopItemListPosition? after = null;
        if (request.Cursor is not null)
        {
            var decoded = PageCursor.Decode(request.Cursor, scope);
            if (!decoded.Succeeded || !Guid.TryParseExact(decoded.Value!.TieBreaker, "N", out var afterId))
            {
                return OperationResult.Failure<CursorPage<ShopItemListRow>>(
                    OperationError.Of(ErrorCodes.CursorInvalid, "The cursor is not valid for this list."));
            }

            after = new ShopItemListPosition(decoded.Value.SortValue, afterId);
        }

        var limit = new PageRequest(request.Cursor, request.Limit).EffectiveLimit;
        var rows = await dataLayer.ListAsync(after, limit + 1, request.Status, search, category, cancellationToken);

        string? next = null;
        if (rows.Count > limit)
        {
            rows.RemoveAt(limit);
            var last = rows[^1];
            next = new PageCursor(last.Title, last.Id.ToString("N")).Encode(scope);
        }

        return OperationResult.Success(new CursorPage<ShopItemListRow>(rows, next));
    }

    public async Task<OperationResult<ShopItemDetail>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await dataLayer.FindAsync(id, cancellationToken);
        return item is null ? NotFound() : OperationResult.Success(ToDetail(item));
    }

    public async Task<OperationResult<ShopItemDetail>> CreateAsync(
        ShopItemCreateViewModel create, ShopItemMediaCheck media, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(media);

        var title = create.Title.Trim();
        var slug = create.Slug ?? Slugs.FromName(title);

        var (description, descriptionError) = PrepareDescription(create.Description, media);
        if (descriptionError is not null)
        {
            return OperationResult.Failure<ShopItemDetail>(descriptionError);
        }

        if (await dataLayer.SlugExistsAsync(slug, exceptId: null, cancellationToken))
        {
            return OperationResult.Failure<ShopItemDetail>(SlugTaken(slug));
        }

        var gallery = create.ImageMediaIds ?? [];
        if (GalleryError(gallery, media, isPublic: false) is { } galleryError)
        {
            return OperationResult.Failure<ShopItemDetail>(galleryError);
        }

        if (StockConflict(create.OneOfAKind == true, create.StartingStockQuantity) is { } stockError)
        {
            return OperationResult.Failure<ShopItemDetail>(stockError);
        }

        var now = clock.UtcNow;
        var item = new ShopItem
        {
            Id = Guid.CreateVersion7(now),
            Title = title,
            Slug = slug,
            ArtistId = create.ArtistId,
            OneOfAKind = create.OneOfAKind == true,
            StartingPriceCents = create.StartingPriceCents,
            ItemNumber = await dataLayer.NextItemNumberAsync(cancellationToken),
            Status = PublishStatus.Draft,
        };
        item.StartingStockQuantity = StockFor(item.OneOfAKind, create.StartingStockQuantity);
        SetDescription(item, description);
        ReplaceCategories(item, create.Categories ?? []);
        ReplaceGallery(item, gallery);

        var sku = await ResolveSkuAsync(item, create.Sku, cancellationToken);
        if (!sku.Succeeded)
        {
            return OperationResult.Failure<ShopItemDetail>(sku.Error!);
        }

        item.Sku = sku.Value!;
        var changes = new SkuChanges([new SkuReservation(item.Sku, item.Id)], []);
        var summary = AuditSummary("Created as Draft", [], description);

        return await dataLayer.CreateAsync(item, changes, actorUserId, summary, cancellationToken) switch
        {
            ShopWriteOutcome.Saved => OperationResult.Success(ToDetail(item)),
            ShopWriteOutcome.SlugTaken => OperationResult.Failure<ShopItemDetail>(SlugTaken(slug)),
            ShopWriteOutcome.SkuTaken => OperationResult.Failure<ShopItemDetail>(SkuTaken(item.Sku)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<ShopItemDetail>> UpdateAsync(
        Guid id,
        ShopItemUpdateViewModel update,
        string? ifMatch,
        ShopItemMediaCheck? media,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        var item = await dataLayer.FindAsync(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(item.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<ShopItemDetail>(versionError);
        }

        var isPublic = item.Status is PublishStatus.Published or PublishStatus.Scheduled;

        // Checked before anything is mutated: a refusal must leave the tracked item as it was found.
        if (update.ImageMediaIds is { } wantedGallery
            && !GalleryOf(item).SequenceEqual(wantedGallery)
            && GalleryError(wantedGallery, media, isPublic) is { } galleryRefusal)
        {
            return OperationResult.Failure<ShopItemDetail>(galleryRefusal);
        }

        if (StockConflict(update.OneOfAKind ?? item.OneOfAKind, update.StartingStockQuantity) is { } stockRefusal)
        {
            return OperationResult.Failure<ShopItemDetail>(stockRefusal);
        }

        var changes = new List<string>();

        if (update.Title is not null && update.Title.Trim() is var title && !string.Equals(title, item.Title, StringComparison.Ordinal))
        {
            item.Title = title;
            changes.Add("title");
        }

        PublishedPathChange? pathChange = null;
        if (update.Slug is not null && !string.Equals(update.Slug, item.Slug, StringComparison.Ordinal))
        {
            if (await dataLayer.SlugExistsAsync(update.Slug, item.Id, cancellationToken))
            {
                return OperationResult.Failure<ShopItemDetail>(SlugTaken(update.Slug));
            }

            // A slug that has ever been public is someone's bookmark: the old address is redirected to the new one,
            // staged with this save (content.md, ADR 0035).
            pathChange = PublishedPathChange.For(ShopPolicy.SitePathPrefix, item.Slug, update.Slug, item.PublishedAt);
            item.Slug = update.Slug;
            changes.Add(pathChange is null ? "slug" : $"slug (redirecting {pathChange.FromPath})");
        }

        SanitizedHtml? description = null;
        if (update.Description is not null)
        {
            (description, var descriptionError) = PrepareDescription(update.Description, media ?? ShopItemMediaCheck.None);
            if (descriptionError is not null)
            {
                return OperationResult.Failure<ShopItemDetail>(descriptionError);
            }

            var stored = description!.IsEmpty ? null : description.Html;
            if (!string.Equals(stored, item.Description, StringComparison.Ordinal))
            {
                // content.md: what the site needs is checked at publish, and published content keeps it.
                if (stored is null && isPublic)
                {
                    return OperationResult.Failure<ShopItemDetail>(OperationError.Validation(
                        "This item is published, so it keeps a description.",
                        [("description", "The site's product page and search results show it. Edit it rather than clearing it.")]));
                }

                SetDescription(item, description);
                changes.Add(stored is null ? "description cleared" : "description");
            }
        }

        if (update.ArtistId is { } artistId && artistId != item.ArtistId)
        {
            item.ArtistId = artistId;
            changes.Add("artist");
        }
        else if (update.RemoveArtist == true && item.ArtistId is not null)
        {
            item.ArtistId = null;
            changes.Add("artist removed");
        }

        if (update.Categories is { } categories && !CategoriesOf(item).SequenceEqual(categories, StringComparer.Ordinal))
        {
            ReplaceCategories(item, categories);
            changes.Add(categories.Count == 0 ? "categories cleared" : "categories");
        }

        if (update.ImageMediaIds is { } gallery && !GalleryOf(item).SequenceEqual(gallery))
        {
            ReplaceGallery(item, gallery);
            changes.Add(gallery.Count == 0 ? "photographs cleared" : "photographs");
        }

        // ADR 0038: the flag and the starting count are draft fields only until Square holds the record. From then
        // on quantity is the set-quantity command (5.4b), which one-of-a-kind artwork never gets.
        var wantsOneOfAKind = update.OneOfAKind ?? item.OneOfAKind;
        var wantsStock = StockFor(wantsOneOfAKind, update.StartingStockQuantity ?? item.StartingStockQuantity);
        if (wantsOneOfAKind != item.OneOfAKind || wantsStock != item.StartingStockQuantity)
        {
            if (!StartingStockEditable(item))
            {
                return OperationResult.Failure<ShopItemDetail>(OperationError.Validation(
                    "Square holds this item's stock now.",
                    [(update.OneOfAKind is not null ? "oneOfAKind" : "startingStockQuantity",
                        "Use the set-quantity action instead: the starting count only applies before the item reaches Square.")]));
            }

            if (wantsOneOfAKind != item.OneOfAKind)
            {
                changes.Add(wantsOneOfAKind ? "one of a kind" : "not one of a kind");
            }

            if (wantsStock != item.StartingStockQuantity)
            {
                changes.Add("starting quantity");
            }

            item.OneOfAKind = wantsOneOfAKind;
            item.StartingStockQuantity = wantsStock;
        }

        if (update.StartingPriceCents is not null || update.RemoveStartingPrice == true)
        {
            var wanted = update.RemoveStartingPrice == true ? (long?)null : update.StartingPriceCents;
            if (wanted != item.StartingPriceCents)
            {
                if (!StartingPriceEditable(item))
                {
                    return OperationResult.Failure<ShopItemDetail>(OperationError.Validation(
                        "Square holds this item's price now.",
                        [("startingPriceCents", "Use the change-price action instead: the starting price only applies before the item reaches Square.")]));
                }

                item.StartingPriceCents = wanted;
                changes.Add(wanted is null ? "starting price cleared" : "starting price");
            }
        }

        var skuChanges = SkuChanges.None;
        if (SkuPolicy.Normalize(update.Sku) is { } wantedSku && !string.Equals(wantedSku, item.Sku, StringComparison.Ordinal))
        {
            var owners = await dataLayer.SkuOwnersAsync([wantedSku], cancellationToken);
            if (owners.TryGetValue(wantedSku, out var owner)
                && !(owner.OwnerType == CatalogOwnerTypes.ShopItem && owner.OwnerId == item.Id))
            {
                return OperationResult.Failure<ShopItemDetail>(SkuTaken(wantedSku));
            }

            skuChanges = new SkuChanges(
                [new SkuReservation(wantedSku, item.Id)],
                [new SkuReservation(item.Sku, item.Id)]);
            item.Sku = wantedSku;
            changes.Add("SKU");
        }

        if (changes.Count == 0)
        {
            return OperationResult.Success(ToDetail(item));
        }

        var auditLine = AuditSummary("Updated", changes, description);
        return await dataLayer.UpdateAsync(item, skuChanges, actorUserId, auditLine, pathChange, cancellationToken) switch
        {
            ShopWriteOutcome.Saved => OperationResult.Success(ToDetail(item)),
            ShopWriteOutcome.SlugTaken => OperationResult.Failure<ShopItemDetail>(SlugTaken(item.Slug)),
            ShopWriteOutcome.SkuTaken => OperationResult.Failure<ShopItemDetail>(SkuTaken(item.Sku)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<ShopItemDetail>> TransitionAsync(
        Guid id,
        PublishCommand command,
        string? ifMatch,
        ShopItemMediaCheck? media,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        var item = await dataLayer.FindAsync(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(item.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<ShopItemDetail>(versionError);
        }

        var from = item.Status;
        if (PublishLifecycle.Next(from, command) is not { } to)
        {
            return OperationResult.Failure<ShopItemDetail>(TransitionInvalid(from, command));
        }

        if (command == PublishCommand.Publish)
        {
            if (PublishRequirementError(item, media) is { } requirementError)
            {
                return OperationResult.Failure<ShopItemDetail>(requirementError);
            }

            item.PublishedAt ??= clock.UtcNow;
            item.ScheduledFor = null;
        }
        else if (command == PublishCommand.Archive)
        {
            // An archived item is never published by the scheduled publisher, so a time left on it would only
            // mislead the staff reading it.
            item.ScheduledFor = null;
        }

        item.Status = to;

        return await dataLayer.TransitionAsync(item, command, from, actorUserId, cancellationToken) switch
        {
            ShopWriteOutcome.Saved => OperationResult.Success(ToDetail(item)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<ShopItemDetail>> ScheduleAsync(
        Guid id,
        DateTimeOffset scheduledFor,
        string? ifMatch,
        ShopItemMediaCheck? media,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        var item = await dataLayer.FindAsync(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(item.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<ShopItemDetail>(versionError);
        }

        var from = item.Status;
        if (PublishLifecycle.Next(from, PublishCommand.Schedule) is not { } to)
        {
            return OperationResult.Failure<ShopItemDetail>(TransitionInvalid(from, PublishCommand.Schedule));
        }

        // A time already past would be published by the next pass, a minute later, which is not what "schedule"
        // means: staff who want it out now have the publish command for that.
        if (scheduledFor <= clock.UtcNow)
        {
            return OperationResult.Failure<ShopItemDetail>(OperationError.Validation(
                "Choose a time in the future.",
                [("scheduledFor", "This time has passed. Publish the item now instead, or pick a later time.")]));
        }

        // Checked now as well as at publish, so staff hear about a missing description, price or photograph while
        // they are looking at the screen rather than at midnight (ADR 0036).
        if (PublishRequirementError(item, media) is { } requirementError)
        {
            return OperationResult.Failure<ShopItemDetail>(requirementError);
        }

        item.ScheduledFor = scheduledFor;
        item.Status = to;

        return await dataLayer.TransitionAsync(item, PublishCommand.Schedule, from, actorUserId, cancellationToken) switch
        {
            ShopWriteOutcome.Saved => OperationResult.Success(ToDetail(item)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<ShopItemDetail>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        var item = await dataLayer.FindAsync(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(item.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<ShopItemDetail>(versionError);
        }

        var from = item.Status;
        if (PublishLifecycle.Next(from, PublishCommand.Unschedule) is not { } to)
        {
            return OperationResult.Failure<ShopItemDetail>(TransitionInvalid(from, PublishCommand.Unschedule));
        }

        item.ScheduledFor = null;
        item.Status = to;

        return await dataLayer.TransitionAsync(item, PublishCommand.Unschedule, from, actorUserId, cancellationToken) switch
        {
            ShopWriteOutcome.Saved => OperationResult.Success(ToDetail(item)),
            _ => Conflict(),
        };
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        dataLayer.ListDueAsync(asOf, take, cancellationToken);

    public async Task<ScheduledPublishOutcome> PublishScheduledAsync(
        Guid id, DateTimeOffset asOf, ShopItemMediaCheck? media, CancellationToken cancellationToken)
    {
        var item = await dataLayer.FindAsync(id, cancellationToken);

        // Read again inside this scope, because the due list is a snapshot: staff may have published, rescheduled
        // or archived the item since, and another Worker may have taken it.
        if (item is null
            || item.Status != PublishStatus.Scheduled
            || item.ScheduledFor is null
            || item.ScheduledFor > asOf)
        {
            return ScheduledPublishOutcome.Skipped;
        }

        // The gallery the facade asked Media about is no longer this item's: whoever changed it is mid-edit, so this
        // pass leaves the item to them and the next one asks again.
        if (!GalleryOf(item).ToHashSet().SetEquals(media?.AskedGallery ?? new HashSet<Guid>()))
        {
            return ScheduledPublishOutcome.Lost;
        }

        var from = item.Status;
        if (PublishRequirementError(item, media) is { } requirementError)
        {
            // Back to Draft rather than left Scheduled: a record whose time has passed would otherwise be retried
            // every minute for ever, and nobody would be told (ADR 0036).
            item.ScheduledFor = null;
            item.Status = PublishStatus.Draft;

            return await dataLayer.AbandonScheduleAsync(item, from, requirementError.Message, cancellationToken) switch
            {
                ShopWriteOutcome.Saved => ScheduledPublishOutcome.Unscheduled,
                _ => ScheduledPublishOutcome.Lost,
            };
        }

        item.PublishedAt ??= clock.UtcNow;
        item.ScheduledFor = null;
        item.Status = PublishStatus.Published;

        return await dataLayer.TransitionAsync(
            item, PublishCommand.Publish, from, ScheduledPublishPolicy.Actor, cancellationToken) switch
        {
            ShopWriteOutcome.Saved => ScheduledPublishOutcome.Published,

            // The row version moved under this save: another Worker published it, or staff changed it. Either way
            // it is theirs now.
            _ => ScheduledPublishOutcome.Lost,
        };
    }

    public async Task<int> SeedAsync(IReadOnlyList<ShopItemSeed> seeds, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seeds);

        var now = clock.UtcNow;
        var taken = await dataLayer.GetSlugsAsync(cancellationToken);
        var number = await dataLayer.NextItemNumberAsync(cancellationToken);
        var items = new List<ShopItem>();
        var reservations = new List<SkuReservation>();

        // Every SKU the seed could suggest, asked once: a seed is not worth failing the whole migration over, so an
        // item whose suggestion someone has taken is skipped like one whose slug is taken (ADR 0037).
        var owned = await dataLayer.SkuOwnersAsync(
            [.. Enumerable.Range(number, seeds.Count).Select(SkuPolicy.ForShopItem)], cancellationToken);

        foreach (var seed in seeds)
        {
            var slug = Slugs.FromName(seed.Title);
            if (!taken.Add(slug))
            {
                continue;
            }

            var sku = SkuPolicy.ForShopItem(number);
            if (owned.ContainsKey(sku))
            {
                number++;
                continue;
            }

            var item = new ShopItem
            {
                Id = Guid.CreateVersion7(now),
                Title = seed.Title,
                Slug = slug,
                ArtistId = seed.ArtistId,
                Sku = sku,
                ItemNumber = number++,
                OneOfAKind = seed.OneOfAKind,
                StartingPriceCents = seed.StartingPriceCents,
                StartingStockQuantity = StockFor(seed.OneOfAKind, seed.StartingStockQuantity),
                Status = PublishStatus.Published,
                PublishedAt = now,
            };

            // Seeds go through the same sanitizer as anything staff write: no write path skips it (content.md).
            SetDescription(item, sanitizer.Sanitize(seed.Description));
            ReplaceCategories(item, seed.Categories);
            items.Add(item);
            reservations.Add(new SkuReservation(item.Sku, item.Id));
        }

        return items.Count == 0
            ? 0
            : await dataLayer.AddSeedAsync(items, new SkuChanges(reservations, []), actor, cancellationToken);
    }

    /// <summary>
    /// The SKU a new item takes: the one staff typed, or the first <c>ART-&lt;item&gt;</c> no record in the CMS
    /// holds (ADR 0037). Refuses one another record already has.
    /// </summary>
    private async Task<OperationResult<string>> ResolveSkuAsync(
        ShopItem item, string? requested, CancellationToken cancellationToken)
    {
        if (SkuPolicy.Normalize(requested) is { } typed)
        {
            var owners = await dataLayer.SkuOwnersAsync([typed], cancellationToken);
            return owners.ContainsKey(typed)
                ? OperationResult.Failure<string>(SkuTaken(typed))
                : OperationResult.Success(typed);
        }

        // A handful of candidates in one question rather than one query per attempt. The number is a suggestion
        // aid, so a gap in the sequence costs nothing.
        var candidates = Enumerable.Range(item.ItemNumber, 8).Select(SkuPolicy.ForShopItem).ToList();
        var taken = await dataLayer.SkuOwnersAsync(candidates, cancellationToken);
        var free = candidates.Find(candidate => !taken.ContainsKey(candidate));

        // Every suggestion in the batch is taken: fall back to the item's id, which nothing else can hold. Staff
        // can edit it, and the registry still refuses a duplicate.
        return OperationResult.Success(free ?? string.Create(
            CultureInfo.InvariantCulture,
            $"{SkuPolicy.ArtworkPrefix}-{item.Id.ToString("N")[..8].ToUpperInvariant()}"));
    }

    /// <summary>
    /// content.md: what the site needs is checked at publish, not at draft. The title, slug and SKU are guaranteed
    /// by every write; the description, the price, the starting count and a described photograph are what a draft
    /// may leave unfinished. All are reported at once, so the editor fixes everything in one pass.
    /// </summary>
    private static OperationError? PublishRequirementError(ShopItem item, ShopItemMediaCheck? media)
    {
        var problems = new List<(string Field, string Error)>();

        if (string.IsNullOrWhiteSpace(item.Description))
        {
            problems.Add(("description", "Write a description first: the product page and search results show it."));
        }

        if (item.StartingPriceCents is null)
        {
            problems.Add(("startingPriceCents", "Set what it costs."));
        }

        if (item.StartingStockQuantity is null)
        {
            problems.Add(("startingStockQuantity", "Set how many there are."));
        }

        var gallery = GalleryOf(item).ToList();
        if (gallery.Count == 0)
        {
            problems.Add(("imageMediaIds", "Add at least one photograph: the shop can't show a piece without one."));
        }
        else if (media is null || !media.AskedGallery.SetEquals(gallery))
        {
            // The answer does not cover the gallery the item has: whoever changed it is mid-edit.
            return EntityTag.Conflict(What);
        }
        else if (gallery.Exists(media.MissingGallery.Contains))
        {
            problems.Add(("imageMediaIds", "A photograph is no longer in the media library. Choose another, or remove it."));
        }
        else if (gallery.Count(media.GalleryWithoutAltText.Contains) is var undescribed and > 0)
        {
            problems.Add(("imageMediaIds", undescribed == 1
                ? "A photograph has no alt text. Describe it in Media for people who can't see it."
                : string.Create(CultureInfo.InvariantCulture, $"{undescribed} photographs have no alt text. Describe each in Media.")));
        }

        return problems.Count == 0
            ? null
            : OperationError.Validation("This item isn't ready to publish yet.", problems);
    }

    /// <summary>
    /// Whether Media's answer clears the gallery being saved. The facade asked about these very images; an answer
    /// that does not cover one counts as no answer. A public item's photographs need alt text as they are attached,
    /// not only at publish (content.md).
    /// </summary>
    private static OperationError? GalleryError(
        IReadOnlyList<Guid> gallery, ShopItemMediaCheck? media, bool isPublic)
    {
        if (gallery.Count == 0)
        {
            return null;
        }

        if (media is null || !gallery.All(media.AskedGallery.Contains))
        {
            return EntityTag.Conflict(What);
        }

        if (gallery.Any(media.MissingGallery.Contains))
        {
            return OperationError.Validation(
                "A photograph is not in the media library.",
                [("imageMediaIds", "Choose images from the media library.")]);
        }

        return isPublic && gallery.Any(media.GalleryWithoutAltText.Contains)
            ? OperationError.Validation(
                "This item is published, so a new photograph needs alt text first.",
                [("imageMediaIds", "Add alt text to the image in Media first.")])
            : null;
    }

    /// <summary>
    /// The description as it would be stored, or why it can't be: too long once sanitized, a picture without alt
    /// text, or a picture Media doesn't have (ADR 0034).
    /// </summary>
    private (SanitizedHtml? Description, OperationError? Error) PrepareDescription(string? raw, ShopItemMediaCheck media)
    {
        var description = sanitizer.Sanitize(raw);

        if (description.Refused is { } refusal)
        {
            return (null, OperationError.Validation(
                "The description couldn't be read.",
                [("description", $"{refusal} Paste it as plain text, or simplify its formatting, and try again.")]));
        }

        if (description.Html.Length > ShopPolicy.DescriptionMaxLength)
        {
            return (null, OperationError.Validation(
                "The description is too long.",
                [("description", string.Create(CultureInfo.InvariantCulture, $"Keep it under {ShopPolicy.DescriptionMaxLength:N0} characters of formatted text."))]));
        }

        var problems = new List<(string Field, string Error)>();

        var undescribed = description.Images.Count(image => image.AltText is null);
        if (undescribed > 0)
        {
            problems.Add(("description", undescribed == 1
                ? "A picture has no alt text. Describe it for people who can't see it."
                : string.Create(CultureInfo.InvariantCulture, $"{undescribed} pictures have no alt text. Describe each for people who can't see them.")));
        }

        // Not asked about counts as not vouched for: the facade's answer must cover the very description being stored.
        if (description.MediaAssetIds.Any(id => !media.AskedDescription.Contains(id) || media.MissingDescription.Contains(id)))
        {
            problems.Add(("description", "A picture isn't in the media library, or couldn't be checked. Remove it or choose another, then save again."));
        }

        return problems.Count == 0
            ? (description, null)
            : (null, OperationError.Validation("The description needs attention.", problems));
    }

    /// <summary>Stores the sanitized description and makes the image references exactly the pictures it shows.</summary>
    private static void SetDescription(ShopItem item, SanitizedHtml? description)
    {
        item.Description = description is null || description.IsEmpty ? null : description.Html;

        var wanted = description?.MediaAssetIds ?? [];
        item.DescriptionImages.RemoveAll(reference => !wanted.Contains(reference.MediaAssetId));
        foreach (var assetId in wanted.Where(assetId => item.DescriptionImages.All(reference => reference.MediaAssetId != assetId)))
        {
            item.DescriptionImages.Add(new ShopItemMediaReference { ShopItemId = item.Id, MediaAssetId = assetId });
        }
    }

    /// <summary>Makes the categories exactly <paramref name="categories"/>. The validator has checked the allow-list.</summary>
    private static void ReplaceCategories(ShopItem item, IReadOnlyList<string> categories)
    {
        item.Categories.RemoveAll(row => !categories.Contains(row.Category, StringComparer.Ordinal));
        foreach (var category in categories.Where(category =>
            item.Categories.All(row => !string.Equals(row.Category, category, StringComparison.Ordinal))))
        {
            item.Categories.Add(new ShopItemCategory { ShopItemId = item.Id, Category = category });
        }
    }

    /// <summary>
    /// Makes the gallery exactly <paramref name="gallery"/>, in that order. An image that stays is renumbered in
    /// place rather than removed and added again, which the (item, image) key would refuse in one save.
    /// </summary>
    private static void ReplaceGallery(ShopItem item, IReadOnlyList<Guid> gallery)
    {
        var existing = item.Images.ToDictionary(image => image.MediaAssetId);
        item.Images.RemoveAll(image => !gallery.Contains(image.MediaAssetId));

        for (var position = 0; position < gallery.Count; position++)
        {
            if (existing.TryGetValue(gallery[position], out var image))
            {
                image.Position = position;
            }
            else
            {
                item.Images.Add(new ShopItemImage
                {
                    ShopItemId = item.Id,
                    MediaAssetId = gallery[position],
                    Position = position,
                });
            }
        }
    }

    /// <summary>One of a kind means exactly one (ADR 0032, 0038): a count left out follows the flag.</summary>
    private static int? StockFor(bool oneOfAKind, int? requested) => oneOfAKind ? 1 : requested;

    /// <summary>
    /// A count other than one sent for a one-of-a-kind piece. Refused rather than quietly replaced by 1, so nobody
    /// types 9, is told the save worked, and finds a 1 (ADR 0038). A count left out follows the flag.
    /// </summary>
    private static OperationError? StockConflict(bool oneOfAKind, int? requested) =>
        oneOfAKind && requested is { } count && count != 1
            ? OperationError.Validation(
                "A one-of-a-kind piece is a count of one.",
                [("startingStockQuantity", "There is only one of it. Untick “one of a kind” to stock more than one.")])
            : null;

    public async Task<OperationResult<CursorPage<ShopItemSiteRow>>> ListForSiteAsync(
        SiteShopListViewModel request, Guid? artistId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sortName = request.Sort ?? ShopPolicy.SiteSorts[0];
        var sort = sortName switch
        {
            "price-asc" => ShopSiteSort.PriceAscending,
            "price-desc" => ShopSiteSort.PriceDescending,
            "title" => ShopSiteSort.Title,
            _ => ShopSiteSort.Newest,
        };

        // The cursor's fingerprint covers every filter and the order: a cursor from "jewelry, by price" replayed on
        // "prints, newest" is refused rather than resuming at a row that means nothing there.
        var scope = string.Create(
            CultureInfo.InvariantCulture,
            $"site-shop|category={request.Category}|artist={request.Artist}|sort={sortName}");

        ShopSitePosition? after = null;
        if (request.Cursor is not null)
        {
            var decoded = PageCursor.Decode(request.Cursor, scope);
            if (!decoded.Succeeded
                || !Guid.TryParseExact(decoded.Value!.TieBreaker, "N", out var afterId)
                || PositionOf(sort, decoded.Value.SortValue, afterId) is not { } position)
            {
                return OperationResult.Failure<CursorPage<ShopItemSiteRow>>(
                    OperationError.Of(ErrorCodes.CursorInvalid, "The cursor is not valid for this list."));
            }

            after = position;
        }

        var limit = new PageRequest(request.Cursor, request.Limit).EffectiveLimit;
        var rows = await dataLayer.ListPublishedAsync(
            new ShopSiteQuery(request.Category, artistId, sort, after, limit + 1), cancellationToken);

        string? next = null;
        if (rows.Count > limit)
        {
            rows.RemoveAt(limit);
            var last = rows[^1];
            next = new PageCursor(SortValueOf(sort, last), last.Id.ToString("N")).Encode(scope);
        }

        return OperationResult.Success(new CursorPage<ShopItemSiteRow>(rows, next));
    }

    public Task<ShopItemSiteRow?> GetForSiteAsync(string slug, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        return dataLayer.FindPublishedAsync(slug, cancellationToken);
    }

    public async Task<OperationResult<PreviewRecord<ShopItemSiteRow>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await dataLayer.FindAsync(id, cancellationToken);
        return item is null
            ? OperationResult.Failure<PreviewRecord<ShopItemSiteRow>>(OperationError.Of(ErrorCodes.NotFound, "There is no such item."))
            : OperationResult.Success(new PreviewRecord<ShopItemSiteRow>(
                new ShopItemSiteRow(
                    item.Id,
                    item.Slug,
                    item.Title,
                    item.Description,
                    item.ArtistId,
                    [.. CategoriesOf(item)],
                    [.. GalleryOf(item)],
                    item.PriceCents,
                    item.OneOfAKind,
                    item.SoldOut,
                    item.PublishedAt,
                    item.UpdatedAt),
                item.Status));
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountForSiteByArtistAsync(CancellationToken cancellationToken) =>
        await dataLayer.CountPublishedByArtistAsync(cancellationToken);

    public async Task<IReadOnlyList<SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken) =>
        [.. (await dataLayer.ListPublishedAddressesAsync(cancellationToken))
            .Select(row => new SitemapEntryServiceModel(SitePaths.AddressOf(ShopPolicy.SitePathPrefix, row.Slug), row.UpdatedAt))];

    /// <summary>
    /// The keyset value a cursor carries for <paramref name="sort"/>: the first publish (round-trip format), the price
    /// in cents, or the title. An empty value is "unpriced" (or, for newest, never published — which a published row
    /// is not).
    /// </summary>
    private static string SortValueOf(ShopSiteSort sort, ShopItemSiteRow row) => sort switch
    {
        ShopSiteSort.PriceAscending or ShopSiteSort.PriceDescending =>
            row.PriceCents?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        ShopSiteSort.Title => row.Title,
        _ => row.PublishedAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
    };

    /// <summary>The position a cursor's keyset value names, or <c>null</c> when it does not parse for the order.</summary>
    private static ShopSitePosition? PositionOf(ShopSiteSort sort, string sortValue, Guid id)
    {
        switch (sort)
        {
            case ShopSiteSort.PriceAscending or ShopSiteSort.PriceDescending:
                if (sortValue.Length == 0)
                {
                    return new ShopSitePosition(id);
                }

                return long.TryParse(sortValue, NumberStyles.None, CultureInfo.InvariantCulture, out var price)
                    ? new ShopSitePosition(id, PriceCents: price)
                    : null;

            case ShopSiteSort.Title:
                return new ShopSitePosition(id, Title: sortValue);

            default:
                return DateTimeOffset.TryParseExact(
                    sortValue, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var publishedAt)
                    ? new ShopSitePosition(id, PublishedAt: publishedAt)
                    : null;
        }
    }

    private static IEnumerable<string> CategoriesOf(ShopItem item) =>
        item.Categories.Select(row => row.Category).Order(StringComparer.Ordinal);

    private static IEnumerable<Guid> GalleryOf(ShopItem item) =>
        item.Images.OrderBy(image => image.Position).Select(image => image.MediaAssetId);

    /// <summary>
    /// Whether the CMS may still set the starting price: only until Square has reported money or stock, after which
    /// price is Square's record and a change is a command (ADR 0032, 0037).
    /// </summary>
    private static bool StartingPriceEditable(ShopItem item) => !item.HasSquareRecord;

    /// <summary>
    /// Whether the CMS may still set the one-of-a-kind flag and the starting count: the same gate (ADR 0038).
    /// </summary>
    private static bool StartingStockEditable(ShopItem item) => !item.HasSquareRecord;

    /// <summary>The audit line, with what the sanitizer removed (in words) when it removed anything.</summary>
    private static string AuditSummary(string action, List<string> changes, SanitizedHtml? description)
    {
        var summary = changes.Count == 0 ? $"{action}." : $"{action}: {string.Join(", ", changes)}.";
        if (description is { Removed.Count: > 0 })
        {
            summary += $" Sanitizer removed: {string.Join("; ", description.Removed)}.";
        }

        return summary.Length <= AuditPolicy.SummaryMaxLength ? summary : summary[..(AuditPolicy.SummaryMaxLength - 1)] + "…";
    }

    private static ShopItemDetail ToDetail(ShopItem item) => new(
        item.Id,
        item.Title,
        item.Slug,
        item.Description,
        item.ArtistId,
        [.. CategoriesOf(item)],
        [.. GalleryOf(item)],
        [.. item.DescriptionImages.Select(reference => reference.MediaAssetId)],
        item.Sku,
        item.ItemNumber,
        item.OneOfAKind,
        item.StartingPriceCents,
        item.StartingStockQuantity,
        CatalogPolicy.Currency,
        StartingPriceEditable(item),
        StartingStockEditable(item),
        item.PriceCents,
        item.StockOnHand,
        item.SoldOut,
        item.HasSquareRecord,
        item.Status,
        item.PublishedAt,
        item.ScheduledFor,
        item.LegacyWordPressId,
        item.CreatedAt,
        item.UpdatedAt,
        item.UpdatedBy,
        EntityTag.Format(item.RowVersion));

    private static OperationError TransitionInvalid(PublishStatus from, PublishCommand command) => OperationError.Of(
        ContentErrorCodes.StatusTransitionInvalid,
        $"This item is {from.ToString().ToLowerInvariant()}, so “{command.Verb()}” doesn't apply. Reload it to see its current status.");

    private static OperationResult<ShopItemDetail> NotFound() =>
        OperationResult.Failure<ShopItemDetail>(OperationError.Of(ErrorCodes.NotFound, "There is no such shop item."));

    private static OperationResult<ShopItemDetail> Conflict() =>
        OperationResult.Failure<ShopItemDetail>(EntityTag.Conflict(What));

    private static OperationError SlugTaken(string slug) => new(
        ContentErrorCodes.SlugTaken,
        "Another shop item already has this web address.",
        new Dictionary<string, string[]>(StringComparer.Ordinal) { ["slug"] = [$"“{slug}” is taken. Choose another."] });

    private static OperationError SkuTaken(string sku) => new(
        CatalogErrorCodes.SkuTaken,
        $"The SKU “{sku}” is already in use.",
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["sku"] = [$"“{sku}” belongs to another shop item or class session. Choose another."],
        });
}
