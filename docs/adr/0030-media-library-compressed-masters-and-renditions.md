# 0030 — Media library: SkiaSharp, compressed masters, metadata allow-list and content-addressed renditions

- **Status:** Accepted — except "Publishing an image", which is **DECIDE** (below)
- **Date:** 2026-10-20
- **Rules affected:** `.claude/rules/media.md`, `.claude/rules/outbox.md`, `.claude/rules/gateway.md`,
  `.claude/rules/api-contract.md`, `.claude/rules/frontend.md`
- **Builds on:** [0003](0003-sql-transactional-outbox-no-broker.md) (renditions and purges are outbox messages),
  [0021](0021-api-host-conventions-and-internal-token-verification.md) (error-code map),
  [0022](0022-gateway-edge-policy-and-internal-token-transform.md) (per-route body limits),
  [0027](0027-admin-shell-and-staff-sign-in-page.md) (admin HTTP setup — **amended**: the CMS no longer uses
  `withFetch()`).

## Context

Prompt 2.3 builds the Media module: uploads, renditions, delivery, alt text and delete. Two requests were added to
the prompt and confirmed at plan approval:

- **"Compress images using native .NET technologies — no external paid NuGets or plugins."**
- **"The uploaded version is compressed and treated as the original, with no loss of quality — this is for the web
  and 2 MB images are too large."** This amends `media.md`, which said an original is the staff upload, immutable.

The forces:

- The site runs on Linux in Azure Container Apps, so a Windows-only library cannot serve.
- No fully managed, first-party .NET library encodes WebP, which `media.md` requires for renditions.
- "No loss of quality" and "2 MB is too large" pull against each other for photos. Re-encoding a JPEG losslessly
  saves almost nothing; the large saving comes from resolution. A dense 2900 × 5367 test photo (5.2 MB) measured:

  | Master | JPEG q92 4:4:4 | PSNR | WebP q92 | PSNR |
  |---|---|---|---|---|
  | 1729 × 3200 | 1.5 MB | 40.9 dB | 975 KB | 41.0 dB |
  | **1383 × 2560** | **988 KB** | **40.4 dB** | 649 KB | 40.3 dB |
  | 1107 × 2048 | 652 KB | 39.9 dB | 431 KB | 39.6 dB |

  About 40 dB PSNR is the usual threshold for "no visible loss". Typical phone photos compress further than this
  worst case.
- EXIF from phones carries GPS. `media.md` says to strip location and keep copyright.

## Decision

### Library: SkiaSharp 4.153.1 (MIT)

SkiaSharp (Microsoft / .NET Foundation, MIT) with `SkiaSharp.NativeAssets.Linux.NoDependencies`, the Linux build with
no fontconfig dependency, for the slim container image. Only `Modules/Media/Gateways/SkiaImageCodecGateway.cs` names
it, behind `IImageCodecGateway`.

| Rejected | Why |
|---|---|
| System.Drawing.Common | Windows-only since .NET 7; no WebP encoder |
| ImageSharp | Six Labors Split License: free to a nonprofit today, but a commercially licensed package, against "no paid" |
| Magick.NET, NetVips | Much larger native dependencies (ImageMagick; LGPL libvips) with nothing gained over Skia |

### The stored original is a compressed master

The upload is not kept. What is stored, checksummed and called the original is a **master** made from it:

1. **Downscaled** only if its longest side exceeds **2560 px** (1.6× the widest rendition).
2. **Re-encoded in its own format:** JPEG q92 with 4:4:4 chroma; WebP lossy q92, or lossless if the source was
   lossless; PNG lossless (zlib 9, all filters).
3. **The EXIF orientation is applied to the pixels**, and the colour profile (ICC) is kept.
4. **Not resized?** Then the upload itself (scrubbed) competes. The re-encode replaces it only if it saves at least
   **10 %** for a lossy format, or any amount for a lossless one. A modest JPEG is never put through a second
   generation of lossy compression for no gain.

Lossless formats are pixel-exact: pixels are decoded unpremultiplied and orientation is applied by remapping
them, so a PNG master equals its upload pixel for pixel. Once stored, the master is immutable.

Business makes every one of these choices (`MasterPlan`, including `KeepsReencode`). The DataLayer returns both
candidates and carries out whatever Business picked.

Every upload is fully decoded in the request, even when the scrubbed upload is kept, so a file that will not decode
is refused while the person is still looking at it. Decodes are bounded per process (`min(2, cores / 2)` slots), and
JPEGs decode at a reduced scale when they will be downscaled.

**The decoder enforces the limits as well.** It checks its own reading of the size against the side and pixel limits
before allocating, because two parsers can disagree about a crafted file. The worst case at 50 MP is about 200 MB per
decoded copy, and up to three copies exist at once (decode, oriented, premultiplied). Size the container for that
(below).

### Metadata is an allow-list, written by managed code

`ImageProbe` identifies the file by signature and reads its dimensions from the header, before anything decodes it.
`ImageMetadataRewriter` copies the container losslessly and keeps only:

- **EXIF:** Artist, Copyright and (when not applied) Orientation, rebuilt as a fresh TIFF structure.
- **Colour and geometry:** JFIF, ICC, Adobe APP14; PNG `iCCP` / `sRGB` / `gAMA` / `cHRM` / `pHYs`; WebP `ICCP`.

Everything else is dropped by never being copied: the GPS IFD, maker notes, thumbnails, XMP, IPTC, comments, PNG
text chunks, and anything after a JPEG's end marker, where phones append MPF depth and gain maps that carry their
own EXIF.

JPEG segments are an allow-list as well: the coding segments plus the three metadata segments above. A reserved
marker (JPGn) is never copied. A file with more than 4096 segments or chunks is refused, and a WebP keeps at most one
of each chunk it is allowed. **Artist is kept on purpose:** it is attribution, beside Copyright, and staff choose
what `Credit` shows. An allow-list cannot leak what it does not name; a deny-list must know every place a location hides.
Renditions carry no EXIF at all.

The upload's Copyright fills `Credit` when staff give none.

### Blob layout and content addressing

All under the private `media` container, every key under the asset's id:

- `{assetId}/original-{sha256}.{ext}`
- `{assetId}/{masterSha256}/{width}-{profile}.{webp|jpg}`

`profile` is a hash of the encoder settings: preset, qualities, chroma, resampler and a version. Changing an asset's
preset produces new keys rather than overwriting files a browser may hold as immutable. A redelivered
`media.rendition-requested` finds every row already at its key and encodes nothing.

Rendition runs never conflict with staff edits. The Worker writes the original's status with a targeted update,
outside the row-version check, so typing alt text while renditions are made blocks neither side. Every rendition row
it writes gets a new row version, so a slower run for an older preset collides instead of overwriting.

URLs carry `?v=` (the first 12 hex digits of the file's SHA-256). A published file at its current `?v=` is
`public, max-age=31536000, immutable`; without it, or with an old one, it is `public, max-age=300, must-revalidate`
with a strong ETag. A draft is `private, no-cache`. A 404 is `no-store`.

### Delete: a Restrict foreign key, and a purge message

Content that uses an image declares a foreign key to `MediaAsset.Id` with `DeleteBehavior.Restrict`. That includes
an artist's portrait, an exhibition's hero and, in 5.1, `SquareCatalogLink`. `MediaRepository.IsReferencedAsync`
finds every such key in the EF model, so Media never names the modules that use it. The foreign key still fails a
delete that races a new reference, and the DataLayer turns that failure into `media.in_use`.

Deleting the blobs is a side effect of the delete's commit, so it is a second message type,
**`media.purge-requested { assetId }`**. It means "remove every blob under `{assetId}/` that no row names". Two kinds
of write stage it in their own save:

- **A delete**, so everything under the prefix goes.
- **A rendition run that replaced files**, after a preset change, so the superseded renditions go. Nothing calls
  storage after a save.

For a live asset the purge also spares the keys its current preset produces, because a run may be uploading them
ahead of the save that will name them. A rendition message for an asset that no longer exists sweeps the prefix
too, which catches a run that raced a delete.

### Delivery is unversioned and anonymous-by-design

`/media/{id}/{rendition}` is a file URL written into `<img srcset>`, not part of the JSON API. It sits beside
`/webhooks/**` as an unversioned route outside the OpenAPI document. It is `[AllowAnonymous]`. Business refuses a
draft to anyone but signed-in Admin and Editor, and the master (`original`) to everyone else, with the same 404 as a
missing asset.

### Upload edge

The gateway's `cms-media-upload` route (`POST /api/v1/cms/media`, CmsEditor, token-bearing) allows a body of
`MediaPolicy.MaxUploadRequestBytes`, 26 MiB. Every other CMS route keeps the server default. The API action reads the
multipart body itself, so `StreamedMultipartUploadAttribute` removes MVC's form value providers. Text fields must
precede the file. The DataLayer spools the file to a self-deleting temp file and stops one buffer past 25 MiB.

**Every other route is capped at 1 MiB.** `Kestrel:Limits:MaxRequestBodySize` is set to 1 MiB in both the gateway and
the API. The upload route raises it: YARP's per-route `MaxRequestBodySize` at the gateway, and `[RequestSizeLimit]` at
the API. Before this, every route inherited Kestrel's default of about 28.6 MiB.

Field errors are keyed to their own field. An unknown or numeric `compressionPreset` is a 400, as it is on the
`PATCH`.

### Contract additions

`concurrency.precondition_required` → **428**, for a missing `If-Match`, which `api-contract.md` anticipated. An
`If-Match` that is present but not one strong ETag of ours (`*`, `W/…`, a list, garbage) is a 400 `validation.failed`
keyed to `ifMatch`. It is malformed rather than unconditional, and RFC 9110 compares `If-Match` strongly.
Media codes:

| Code | Status |
|---|---|
| `media.type_unsupported` | 415 |
| `media.extension_mismatch` | 415 |
| `media.too_large` | 413 |
| `media.dimensions_out_of_range` | 400 |
| `media.undecodable` | 400 |
| `media.duplicate` | 409, with `existingAssetId` |
| `media.in_use` | 409 |

Media enums serialize as their names. The row version is a `Guid` concurrency token rather than a SQL
`rowversion`, so the same rule holds on the SQLite test database. The Worker's status updates leave it alone.

### The CMS uses the XHR backend

`withFetch()` is removed from the admin app because fetch cannot report upload progress. The admin is
client-rendered only, so fetch's SSR advantages never applied there.

### Publishing an image — **DECIDE**

Every asset is `Visibility = Draft` until content publishing marks it `Published`. Until the first content type
exists, an anonymous visitor gets 404 for every image. **Still open: 2.8 declined it and ADR 0036 moved ownership
to 3.1**, when the site first serves images to anonymous visitors; until then the only cost is that a preview
link's images are staff-only. The options:

1. **The content module calls `IMediaFacade` after its publish commits**, through its `site.revalidate-requested`
   handler or a `media.publish-requested` message of its own. This keeps module ownership, but visibility trails
   publishing by one Worker pass.
2. **Visibility is derived at delivery** from "any published content references it". This is always right, but it
   needs a cross-module read on every image request, which the module rules forbid.
3. **Publish never hides images.** Every image is public once Ready, and only the master stays staff-only. This is
   simplest, but it contradicts `media.md` ("draft-only assets to CMS sessions only").

## Options considered

The library and its alternatives are in the table above. For the master: keeping the raw upload alongside the master
was rejected because it doubles storage and keeps a GPS-bearing copy. A WebP-only master was rejected because Square
catalog images accept JPEG and PNG but not WebP (5.x hands Square the master or the 1600 JPEG).

## Consequences

- **The API and the Worker both carry libSkiaSharp** (about 10 MB). The API decodes uploads, and the Worker makes
  renditions.
- **An upload costs CPU in the request** (a second or two for a large photo), bounded by the decode slots.
- **A failed upload save removes its blob, best effort.** Only a failure of that delete itself leaves an orphan,
  and it is logged.
- **Memory.** The API and Worker containers need about 1 GB of headroom for two concurrent worst-case decodes. Lower
  `MediaPolicy.MaxPixels` rather than starve the container. A decode-slot timeout is follow-up work.
- **Native decoders run in-process.** A crash on a hostile file takes the process down. Keep SkiaSharp current.
- **A content module that references images must use a Restrict foreign key** to `MediaAsset.Id`, and should check
  `IMediaFacade.CheckAttachableAsync` before publishing (alt text required).
- **16-bit PNGs are stored at 8 bits per channel.** For the web, that loses nothing visible.
- **Follow-ups the 2.3 reviews raised and this prompt does not do:**
  - SQL Server integration tests for the Restrict-FK path, the filtered unique index and keyset ordering. The suite
    has no SQL Server container yet, so all of these run on SQLite.
  - A dedicated upload rate limit (the route shares `api`'s 300 a minute).
  - OpenAPI documentation of the `ETag` and `Location` headers and the `existingAssetId` extension.
  - Business still edits the tracked entity, as Identity's does; a domain-record split is a module-wide change.
- **ADR 0027's open gap remains.** A 401 still navigates away at once, so an editor with unsaved alt text loses it
  when the session ends. The "your session ended" dialog that 0027 owes the first editor is not built here, and is
  flagged in the 2.3 report.
