---
paths:
  - src/Arts6311.Domain/Modules/Media/**
  - src/Arts6311.Api/Controllers/**/*Media*
  - src/Arts6311.Tests/Media/**
  - src/web/projects/admin/src/app/features/media/**
---
# Media

Bytes live in blob storage (container `media`). Metadata, relationships and processing status live in SQL.

## Asset kinds

| Kind | Meaning |
|---|---|
| `Original` | The **compressed master** of a staff upload (the upload itself is not kept). Immutable once stored. |
| `Imported` | Pulled from WordPress by the importer, with source URL and import date recorded |
| `Derived` | A rendition (400 / 800 / 1600 wide, WebP + JPEG) linked to its original |
| `Submission` | A picture from the public artist submission form (ADR 0047): 10 MiB cap, always `Draft`, never in the library list; a picture already held is linked, and a staff upload is never refused because of one |

## Required metadata

- MIME type (checked from the file signature, not the extension)
- byte size, width, height
- SHA-256 checksum
- kind and parent asset
- processing status
- alt text (required before an asset can be attached to published content)
- credit/attribution
- uploaded by and when

## The master (ADR 0030)

- **Downscaled** only above **2560 px** on the longest side, then **re-encoded in its own format**: JPEG q92 4:4:4, WebP q92 (lossless stays lossless), PNG lossless. Orientation goes into the pixels; the ICC profile stays.
- **Not resized?** The scrubbed upload competes: a lossy re-encode must save at least 10 % to replace it, a lossless one anything. Never a second lossy generation for nothing.
- **Lossless is pixel-exact.** PNG and lossless-WebP masters equal their upload pixel for pixel; a test holds it.
- **Limits** (`MediaPolicy`): 25 MiB, 16 to 12 000 px a side, 50 MP, read from the header before anything decodes. No animation.
- **Metadata is an allow-list** (`ImageMetadataRewriter`): EXIF Artist, Copyright, Orientation; ICC, JFIF, Adobe. Everything else (GPS, XMP, IPTC, maker notes, thumbnails, text chunks, data after a JPEG's EOI) is dropped by never being copied. Renditions carry no EXIF.
- **SkiaSharp** (MIT) is the image library, named only by `Modules/Media/Gateways/SkiaImageCodecGateway.cs`. The decoder re-checks the limits from its own reading of the header.
- **Business decides; the DataLayer executes.** `MasterPlan` (keep or replace) and `RenditionPlan` (targets, decode size) are Business's. Nothing calls storage after a save, and the Worker's status writes are targeted updates that never take part in a staff editor's row-version check.

## Access and processing

- **Never public blob access.** Delivery is `/media/{assetId}/{rendition}` through the gateway, with long-lived cache headers and ETags. Draft-only assets are served only to CMS sessions.
- **Uploads:**
  - Images only: JPEG, PNG, WebP. No SVG.
  - Size and dimension limits.
  - Streamed through the gateway.
  - EXIF location data stripped; copyright kept.
- **Renditions** are produced by the `media.rendition-requested` outbox handler in the Worker. They're idempotent: content-addressed as `{assetId}/{masterSha256}/{width}-{profile}.{ext}`, where `profile` hashes the preset's encoder settings, so a preset change makes new keys. Never upscaled.
- **URLs carry `?v=`** (12 hex digits of the file's SHA-256). At the current `?v=` a published file is `immutable` for a year; otherwise it revalidates by ETag. Drafts are `private, no-cache`; a 404 is `no-store`.
- **Visibility** is `Draft` until content publishing marks it `Published`. The hand-off is decided (ADR 0042, ADR 0030's option 1): publishing stages `media.publish-requested` with the record's image ids in the same save, and Media's handler marks them `Published`, one way — nothing turns an image back. **It is built in 3.1b; until then every image a site read describes answers 404 to a visitor**, and a preview link's images are staff-only.
- **The site describes an image** through `IMediaFacade.DescribeForSiteAsync` (ADR 0042): its renditions only, the widest JPEG as `src`, never the master; an original with no rendition yet is left out rather than shown broken.
- **Square images.** An image attached to a Square-synced item is uploaded to Square by the catalog push handler. The `SquareCatalogLink` records the Square image id; the asset never stores it.
- **Deleting** an original is blocked while it's referenced by content or by a Square link — **including by a
  soft-deleted record** (ADR 0052). The guard reads past the soft-delete query filter on purpose, because it stands
  in for a `Restrict` foreign key and the database has not forgotten the deleted row; filtered, `media.in_use`
  would reach staff as a 500. So an image a deleted record points at waits: restore the record, clear the image,
  delete it again. **Anything that references an image declares a foreign key to `MediaAsset.Id` with `DeleteBehavior.Restrict`.** The repository finds every such key in the EF model, so Media never names the modules that use it. The bytes go afterwards, through `media.purge-requested`: "remove what no row names under `{assetId}/`". A delete stages it, and so does a rendition run that replaced files. For a live asset it spares the current preset's keys, which a run may be uploading.
- **Before publishing content with an image**, a content module calls `IMediaFacade.CheckAttachableAsync` (alt text required).
- **The site's own pictures** (the logo and the default social image) are ordinary assets at the fixed ids in `MediaSiteImages`. MigrationService's `SiteImageSeeder` puts them there, published, in every environment, through `IMediaFacade.EnsureSiteImageAsync`; a run that finds them changes nothing. Only the seed can choose an id or start an asset `Published`; the CMS route never sets `SiteImageId` (ADR 0048).
- **Alt text** describes what's in the picture. It's never generated from the filename or title.
