---
name: add-media-feature
description: Implement 6311arts media uploads, metadata, renditions, delivery, alt text or Square image hand-off.
---

# Add a Media Feature

Read `.claude/rules/media.md`.

1. **Classify the asset:** Original, Imported or Derived.
2. **Store** bytes in the `media` blob container through the Aspire Blob client. Metadata and status go in SQL.
3. **Validate on upload:**
   - by file signature (`ImageProbe`); the extension must agree
   - size and dimension limits, from the header, before anything decodes (`MediaPolicy`)
   - JPEG/PNG/WebP only, never animated
   - metadata through the allow-list (`ImageMetadataRewriter`): GPS and everything unnamed dropped, copyright kept
   - store the **compressed master** (`MasterPlan`), never the raw upload (ADR 0030)
   - compute a SHA-256 checksum of the stored bytes
4. **Renditions** are produced by the `media.rendition-requested` outbox message, staged in the same save as the upload record. Use `add-outbox-message`. They're content-addressed and idempotent.
5. **Delivery** is through `/media/{id}/{rendition}` with ETag and long-lived cache headers. Draft-only assets are served to CMS sessions only.
6. **Alt text** is required before an asset can be attached to published content.
7. **Square images** are uploaded by the catalog push handler. The Square image id is stored on `SquareCatalogLink`.
8. **Deleting** an original is blocked while it's referenced. A referencing entity declares a `Restrict` foreign key to `MediaAsset.Id`, and the blobs go through `media.purge-requested`.
9. **Image work** goes through `IImageCodecGateway` (SkiaSharp). Nothing else names SkiaSharp.

## Tests

- forged extension rejected
- oversized file rejected
- duplicate rendition run is idempotent
- draft asset not served anonymously
- delete blocked while referenced
- GPS stripped (built by hand in fixtures, never by the code under test)
- lossless master pixel-exact; a large upload downscaled to the cap

## Checklist before done

- [ ] Signature validation, limits, checksum, EXIF policy
- [ ] Renditions via the outbox; delivery via the API route only
- [ ] Alt-text gate; delete protection; tests pass
