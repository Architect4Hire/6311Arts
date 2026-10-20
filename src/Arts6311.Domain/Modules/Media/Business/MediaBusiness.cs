using System.Globalization;
using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Media.Data;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Domain.Modules.Media.Managers.Imaging;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using Arts6311.Domain.Modules.Media.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Media.Business;

/// <summary>
/// The Media module's rules: what an acceptable upload is, how it becomes a master, who may see a draft, and what
/// a rendition run must produce. Every decision is made from the file itself, never from its name or the
/// browser's <c>Content-Type</c> (media.md).
/// </summary>
internal sealed class MediaBusiness(IMediaDataLayer dataLayer, IClock clock) : IMediaBusiness
{
    public async Task<OperationResult<MediaAssetServiceModel>> UploadAsync(
        MediaUploadViewModel upload, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upload);

        await using var spool = await dataLayer.SpoolUploadAsync(upload.Content, MediaPolicy.MaxUploadBytes, cancellationToken);
        if (spool is null)
        {
            return Fail<MediaAssetServiceModel>(
                MediaErrorCodes.TooLarge,
                string.Create(CultureInfo.InvariantCulture, $"Images can be at most {MediaPolicy.MaxUploadBytes / (1024 * 1024)} MB."));
        }

        var fileName = CleanFileName(upload.FileName);
        var probe = ImageProbe.Probe(spool.Stream);
        if (probe is null)
        {
            return Fail<MediaAssetServiceModel>(MediaErrorCodes.TypeUnsupported, "Only JPEG, PNG and WebP images can be uploaded.");
        }

        if (!probe.Format.MatchesExtension(Path.GetExtension(fileName)))
        {
            return Fail<MediaAssetServiceModel>(
                MediaErrorCodes.ExtensionMismatch,
                $"The file is a {probe.Format.MimeType()} but its name says otherwise. Rename it and try again.");
        }

        if (probe.IsAnimated)
        {
            return Fail<MediaAssetServiceModel>(MediaErrorCodes.TypeUnsupported, "Animated images can't be uploaded.");
        }

        if (!probe.HasDimensions)
        {
            return Fail<MediaAssetServiceModel>(MediaErrorCodes.Undecodable, "The image is damaged and can't be read.");
        }

        if (DimensionsError(probe) is { } dimensions)
        {
            return OperationResult.Failure<MediaAssetServiceModel>(dimensions);
        }

        var altText = Clean(upload.AltText);
        if (AltTextError(altText, fileName) is { } altError)
        {
            return OperationResult.Failure<MediaAssetServiceModel>(altError);
        }

        var plan = MasterPlan.For(probe);
        await using var candidates = await dataLayer.PrepareMasterAsync(spool.Stream, probe, plan, cancellationToken);
        if (candidates is null)
        {
            return Fail<MediaAssetServiceModel>(MediaErrorCodes.Undecodable, "The image is damaged and can't be read.");
        }

        // The compression rule (ADR 0030): the re-encode, unless the scrubbed upload is as good for less.
        var keepReencode = plan.KeepsReencode(candidates.Reencoded.Length, candidates.Scrubbed?.Length);
        var masterFile = keepReencode ? candidates.Reencoded : candidates.Scrubbed!;
        var (width, height) = keepReencode ? (candidates.Width, candidates.Height) : (probe.DisplayWidth, probe.DisplayHeight);
        var checksum = await dataLayer.ChecksumAsync(masterFile.Stream, cancellationToken);

        if (await dataLayer.FindDuplicateAsync(checksum, cancellationToken) is { } existing)
        {
            return OperationResult.Failure<MediaAssetServiceModel>(new OperationError(
                MediaErrorCodes.Duplicate,
                "This image is already in the library.",
                new Dictionary<string, string[]>(),
                new Dictionary<string, object?> { ["existingAssetId"] = existing }));
        }

        var now = clock.UtcNow;
        var id = Guid.NewGuid();
        var original = new MediaAsset
        {
            Id = id,
            Kind = MediaAssetKind.Original,
            FileName = fileName,
            MimeType = plan.Format.MimeType(),
            Bytes = masterFile.Length,
            UploadedBytes = spool.Length,
            Width = width,
            Height = height,
            Checksum = checksum,
            BlobKey = MediaBlobKeys.Original(id, checksum, plan.Format),
            ProcessingStatus = MediaProcessingStatus.Pending,
            Visibility = MediaVisibility.Draft,
            CompressionPreset = upload.CompressionPreset ?? MediaCompressionPreset.Balanced,
            AltText = altText,
            Credit = Clean(upload.Credit) ?? Truncate(probe.Exif.Copyright, MediaPolicy.CreditMaxLength),
            UploadedBy = actorUserId,
            UploadedAt = now,
            UpdatedBy = actorUserId,
            UpdatedAt = now,
            RowVersion = Guid.NewGuid(),
        };

        await dataLayer.CreateOriginalAsync(original, masterFile.Stream, cancellationToken);
        return OperationResult.Success(ToServiceModel(original, []));
    }

    public async Task<OperationResult<CursorPage<MediaAssetSummaryServiceModel>>> ListAsync(
        MediaListViewModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var missingAltText = request.MissingAltText ?? false;
        var scope = string.Create(CultureInfo.InvariantCulture, $"media:originals|alt={missingAltText}|status={request.Status}");

        MediaListPosition? after = null;
        if (request.Cursor is not null)
        {
            var decoded = PageCursor.Decode(request.Cursor, scope);
            if (!decoded.Succeeded
                || !long.TryParse(decoded.Value!.SortValue, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || !Guid.TryParseExact(decoded.Value.TieBreaker, "N", out var afterId))
            {
                return OperationResult.Failure<CursorPage<MediaAssetSummaryServiceModel>>(
                    OperationError.Of(ErrorCodes.CursorInvalid, "The cursor is not valid for this list."));
            }

            after = new MediaListPosition(new DateTimeOffset(ticks, TimeSpan.Zero), afterId);
        }

        var limit = new PageRequest(request.Cursor, request.Limit).EffectiveLimit;
        var rows = await dataLayer.ListOriginalsAsync(after, limit + 1, missingAltText, request.Status, cancellationToken);

        string? next = null;
        if (rows.Count > limit)
        {
            rows.RemoveAt(limit);
            var last = rows[^1];
            next = new PageCursor(
                last.UploadedAt.UtcTicks.ToString(CultureInfo.InvariantCulture), last.Id.ToString("N")).Encode(scope);
        }

        return OperationResult.Success(new CursorPage<MediaAssetSummaryServiceModel>(rows, next));
    }

    public async Task<OperationResult<MediaAssetServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var original = await dataLayer.FindOriginalAsync(id, cancellationToken);
        return original is null
            ? NotFound<MediaAssetServiceModel>()
            : OperationResult.Success(ToServiceModel(original, await dataLayer.GetRenditionsAsync(id, cancellationToken)));
    }

    public async Task<OperationResult<MediaAssetServiceModel>> UpdateAsync(
        Guid id, MediaAssetUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        var original = await dataLayer.FindOriginalAsync(id, cancellationToken);
        if (original is null)
        {
            return NotFound<MediaAssetServiceModel>();
        }

        if (VersionError(original, ifMatch) is { } versionError)
        {
            return OperationResult.Failure<MediaAssetServiceModel>(versionError);
        }

        var changes = new List<string>();
        if (update.AltText is not null)
        {
            var altText = Clean(update.AltText);
            if (AltTextError(altText, original.FileName) is { } altError)
            {
                return OperationResult.Failure<MediaAssetServiceModel>(altError);
            }

            if (!string.Equals(altText, original.AltText, StringComparison.Ordinal))
            {
                original.AltText = altText;
                changes.Add(altText is null ? "alt text cleared" : "alt text set");
            }
        }

        if (update.Credit is not null)
        {
            var credit = Clean(update.Credit);
            if (!string.Equals(credit, original.Credit, StringComparison.Ordinal))
            {
                original.Credit = credit;
                changes.Add(credit is null ? "credit cleared" : "credit set");
            }
        }

        var renditionsNeeded = false;
        if (update.CompressionPreset is { } preset && preset != original.CompressionPreset)
        {
            original.CompressionPreset = preset;

            // The library shows it processing again until the Worker has re-encoded it.
            original.ProcessingStatus = MediaProcessingStatus.Pending;
            renditionsNeeded = true;
            changes.Add($"compression preset {preset}");
        }

        if (changes.Count > 0)
        {
            var outcome = await dataLayer.UpdateAsync(
                original, renditionsNeeded, actorUserId, $"Updated: {string.Join(", ", changes)}.", cancellationToken);
            if (outcome == MediaWriteOutcome.Conflict)
            {
                return Conflict<MediaAssetServiceModel>();
            }
        }

        return OperationResult.Success(ToServiceModel(original, await dataLayer.GetRenditionsAsync(id, cancellationToken)));
    }

    public async Task<OperationResult> DeleteAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        var original = await dataLayer.FindOriginalAsync(id, cancellationToken);
        if (original is null)
        {
            return OperationResult.Failure(OperationError.Of(ErrorCodes.NotFound, "There is no such image."));
        }

        if (VersionError(original, ifMatch) is { } versionError)
        {
            return OperationResult.Failure(versionError);
        }

        if (await dataLayer.IsReferencedAsync(id, cancellationToken))
        {
            return OperationResult.Failure(InUse());
        }

        return await dataLayer.DeleteAsync(original, actorUserId, cancellationToken) switch
        {
            MediaWriteOutcome.Saved => OperationResult.Success(),
            MediaWriteOutcome.InUse => OperationResult.Failure(InUse()),
            _ => OperationResult.Failure(ConflictError()),
        };
    }

    public async Task<OperationResult<MediaContentServiceModel>> OpenAsync(
        MediaDeliveryViewModel request, MediaViewer viewer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // One answer for "no such asset", "no such rendition" and "not yours to see": a draft's existence is not
        // something an anonymous visitor may learn either.
        string? rendition;
        if (string.Equals(request.Rendition, MediaPolicy.OriginalRendition, StringComparison.Ordinal))
        {
            if (viewer != MediaViewer.Staff)
            {
                return NotFound<MediaContentServiceModel>();
            }

            rendition = null;
        }
        else
        {
            rendition = MediaRenditionName.TryParse(request.Rendition)?.ToString();
            if (rendition is null)
            {
                return NotFound<MediaContentServiceModel>();
            }
        }

        var record = await dataLayer.FindDeliveryAsync(request.AssetId, rendition, cancellationToken);
        if (record is null || (record.Visibility == MediaVisibility.Draft && viewer != MediaViewer.Staff))
        {
            return NotFound<MediaContentServiceModel>();
        }

        var etag = $"\"{record.Checksum}\"";
        var cacheability = record.Visibility == MediaVisibility.Draft
            ? MediaCacheability.Private
            : string.Equals(request.Version, MediaUrls.VersionOf(record.Checksum), StringComparison.Ordinal)
                ? MediaCacheability.Immutable
                : MediaCacheability.Revalidate;

        if (Matches(request.IfNoneMatch, etag))
        {
            return OperationResult.Success(new MediaContentServiceModel(
                null, record.MimeType, record.Bytes, etag, cacheability, NotModified: true));
        }

        Stream? content = null;
        if (!request.HeadOnly)
        {
            content = await dataLayer.OpenBlobAsync(record.BlobKey, cancellationToken);
            if (content is null)
            {
                return NotFound<MediaContentServiceModel>();
            }
        }

        return OperationResult.Success(new MediaContentServiceModel(
            content, record.MimeType, record.Bytes, etag, cacheability, NotModified: false));
    }

    public async Task<MediaAttachabilityServiceModel> CheckAttachableAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var distinct = ids.Distinct().ToList();
        var found = await dataLayer.FindAttachabilityAsync(distinct, cancellationToken);

        return new MediaAttachabilityServiceModel(
            [.. distinct.Where(id => !found.Exists(row => row.Id == id))],
            [.. found.Where(row => !row.HasAltText).Select(row => row.Id)]);
    }

    public async Task<OperationResult> ProduceRenditionsAsync(
        Guid assetId, int attempt, bool lastAttempt, CancellationToken cancellationToken)
    {
        var original = await dataLayer.FindOriginalAsync(assetId, cancellationToken);

        // Deleted since the message was staged: nothing to render. A run that raced the delete may still have written
        // files after its purge ran, so the prefix is swept again; with no rows left, that is everything under it.
        if (original is null)
        {
            await dataLayer.PurgeAsync(assetId, new HashSet<string>(StringComparer.Ordinal), cancellationToken);
            return OperationResult.Success();
        }

        RenditionRunOutcome outcome;
        try
        {
            outcome = await dataLayer.ProduceRenditionsAsync(original, PlanFor(original), cancellationToken);
        }
        catch (Exception exception) when (lastAttempt && exception is not OperationCanceledException)
        {
            // outbox.md: the last attempt leaves a state staff can see — the library shows the asset as failed.
            await dataLayer.MarkRenditionsFailedAsync(assetId, attempt, cancellationToken);
            throw;
        }

        if (outcome == RenditionRunOutcome.Undecodable)
        {
            // Retrying will not make the master decode, so it fails now rather than after every attempt.
            await dataLayer.MarkRenditionsFailedAsync(assetId, attempt, cancellationToken);
            return OperationResult.Failure(OperationError.Of(MediaErrorCodes.Undecodable, "The master did not decode."));
        }

        return OperationResult.Success();
    }

    public async Task PurgeAsync(Guid assetId, int attempt, bool lastAttempt, CancellationToken cancellationToken)
    {
        // A purge removes only what no row names. For a live asset it also spares the keys its current preset
        // produces: a rendition run may be uploading them right now, ahead of the save that will name them.
        var original = await dataLayer.FindOriginalAsync(assetId, cancellationToken);
        var keep = original is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(
                PlanFor(original).Targets.Select(target => target.BlobKey).Append(original.BlobKey), StringComparer.Ordinal);

        try
        {
            await dataLayer.PurgeAsync(assetId, keep, cancellationToken);
        }
        catch (Exception exception) when (lastAttempt && exception is not OperationCanceledException)
        {
            // The asset is gone, so there is no row to mark; the audit row is the visible state (outbox.md).
            await dataLayer.RecordPurgeFailedAsync(assetId, attempt, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Every rendition, at the asset's preset, keyed by its master and the encoder profile, plus the smallest decode
    /// that serves the widest of them. Renditions are sRGB; nothing is upscaled (the codec clamps the width).
    /// </summary>
    internal static RenditionPlan PlanFor(MediaAsset original)
    {
        var quality = MediaPolicy.QualityFor(original.CompressionPreset);
        var profile = RenditionProfile.For(original.CompressionPreset);

        var targets = MediaRenditionName.All.Select(name => new RenditionTarget(
            name,
            name.Format == MediaImageFormat.Webp ? quality.WebpQuality : quality.JpegQuality,
            name.Format == MediaImageFormat.Jpeg && quality.JpegChroma444,
            ConvertToSrgb: true,
            profile,
            MediaBlobKeys.Rendition(original.Id, original.Checksum, name, profile))).ToList();

        // The widest target, or the master's own width if narrower, scaled to the master's longest side.
        var width = Math.Max(1, original.Width);
        var widest = Math.Min(MediaPolicy.RenditionWidths.Max(), width);
        var decodeLongSide = (int)Math.Ceiling((double)Math.Max(width, original.Height) * widest / width);

        return new RenditionPlan(targets, decodeLongSide);
    }

    internal static MediaAssetServiceModel ToServiceModel(MediaAsset original, IReadOnlyList<MediaAsset> renditions) => new(
        original.Id,
        original.FileName,
        original.MimeType,
        original.Bytes,
        original.UploadedBytes,
        original.Width,
        original.Height,
        original.Checksum,
        original.ProcessingStatus,
        original.Visibility,
        original.CompressionPreset,
        original.AltText,
        original.Credit,
        original.UploadedBy,
        original.UploadedAt,
        original.UpdatedAt,
        EntityTag.Format(original.RowVersion),
        MediaUrls.For(original.Id, MediaPolicy.OriginalRendition, original.Checksum),
        [
            .. renditions
                .Select(rendition => (Row: rendition, Name: MediaRenditionName.TryParse(rendition.RenditionName)))
                .Where(pair => pair.Name is not null)
                .OrderBy(pair => pair.Name!.Width)
                .ThenBy(pair => pair.Name!.Format)
                .Select(pair => new MediaRenditionServiceModel(
                    pair.Row.RenditionName!,
                    pair.Row.MimeType,
                    pair.Row.Width,
                    pair.Row.Height,
                    pair.Row.Bytes,
                    MediaUrls.For(original.Id, pair.Row.RenditionName!, pair.Row.Checksum))),
        ]);

    private static OperationError? DimensionsError(ImageProbeResult probe)
    {
        var shortSide = Math.Min(probe.Width, probe.Height);
        var longSide = Math.Max(probe.Width, probe.Height);
        var pixels = (long)probe.Width * probe.Height;

        if (shortSide >= MediaPolicy.MinSidePixels && longSide <= MediaPolicy.MaxSidePixels && pixels <= MediaPolicy.MaxPixels)
        {
            return null;
        }

        return OperationError.Of(
            MediaErrorCodes.DimensionsOutOfRange,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Images must be at least {MediaPolicy.MinSidePixels} px on each side, at most {MediaPolicy.MaxSidePixels} px on the longest side and at most {MediaPolicy.MaxPixels / 1_000_000} megapixels; this one is {probe.Width}×{probe.Height}."));
    }

    /// <summary>
    /// Alt text describes what is in the picture (media.md). The one mistake detectable without seeing it: pasting
    /// the file name, which describes nothing to a screen-reader user.
    /// </summary>
    private static OperationError? AltTextError(string? altText, string? fileName)
    {
        if (altText is null || fileName is null)
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        return string.Equals(altText, stem, StringComparison.OrdinalIgnoreCase)
            || string.Equals(altText, fileName, StringComparison.OrdinalIgnoreCase)
                ? OperationError.Validation(
                    "One or more fields are not valid.",
                    [("altText", "Describe what's in the picture; the file name isn't a description.")])
                : null;
    }

    private static OperationError? VersionError(MediaAsset original, string? ifMatch)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return OperationError.Of(
                ErrorCodes.PreconditionRequired, "Send the ETag you last read as If-Match, so a newer edit is not overwritten.");
        }

        // Present but not one strong ETag of ours ("*", a weak tag, a list, garbage): the request is malformed, not
        // unconditional (RFC 9110 compares If-Match strongly).
        if (EntityTag.Parse(ifMatch) is not { } version)
        {
            return OperationError.Validation(
                "One or more fields are not valid.",
                [("ifMatch", "If-Match must be the single ETag this image was read with.")]);
        }

        return version != original.RowVersion ? ConflictError() : null;
    }

    /// <summary>Whether an <c>If-None-Match</c> list names this ETag (weak comparison, as RFC 9110 requires for it).</summary>
    private static bool Matches(string? ifNoneMatch, string etag)
    {
        if (string.IsNullOrWhiteSpace(ifNoneMatch))
        {
            return false;
        }

        return ifNoneMatch.Split(',').Select(candidate => candidate.Trim()).Any(candidate =>
            candidate == "*"
            || string.Equals(candidate.StartsWith("W/", StringComparison.Ordinal) ? candidate[2..] : candidate, etag, StringComparison.Ordinal));
    }

    /// <summary>
    /// The name without any path, control characters, invisible format characters (a right-to-left override can make
    /// <c>gpj.exe</c> read as <c>exe.jpg</c>) or excess length. Display only.
    /// </summary>
    private static string CleanFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        name = new string([.. name.Where(character =>
            !char.IsControl(character) && CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.Format)]).Trim();
        return name.Length > MediaPolicy.FileNameMaxLength ? name[^MediaPolicy.FileNameMaxLength..] : name;
    }

    private static string? Truncate(string? text, int length) =>
        text is null || text.Length <= length ? text : text[..length].TrimEnd();

    private static string? Clean(string? text)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static OperationResult<T> Fail<T>(string code, string message) =>
        OperationResult.Failure<T>(OperationError.Of(code, message));

    private static OperationResult<T> NotFound<T>() =>
        OperationResult.Failure<T>(OperationError.Of(ErrorCodes.NotFound, "There is no such image."));

    private static OperationResult<T> Conflict<T>() => OperationResult.Failure<T>(ConflictError());

    private static OperationError ConflictError() =>
        OperationError.Of(ErrorCodes.ConcurrencyConflict, "Someone else changed this image. Reload it and try again.");

    private static OperationError InUse() =>
        OperationError.Of(MediaErrorCodes.InUse, "This image is used by content or the shop, so it can't be deleted.");
}
