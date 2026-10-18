---
name: add-media-feature
description: Implement 6311arts media uploads, metadata, renditions, delivery, alt text or Square image hand-off.
---

# Add a Media Feature

Read `.claude/rules/media.md`.

1. **Classify the asset:** Original, Imported or Derived.
2. **Store** bytes in the `media` blob container through the Aspire Blob client. Metadata and status go in SQL.
3. **Validate on upload:**
   - by file signature
   - size and dimension limits
   - JPEG/PNG/WebP only
   - strip EXIF GPS
   - compute a SHA-256 checksum
4. **Renditions** are produced by the `media.rendition-requested` outbox message, staged in the same save as the upload record. Use `add-outbox-message`. They're content-addressed and idempotent.
5. **Delivery** is through `/media/{id}/{rendition}` with ETag and long-lived cache headers. Draft-only assets are served to CMS sessions only.
6. **Alt text** is required before an asset can be attached to published content.
7. **Square images** are uploaded by the catalog push handler. The Square image id is stored on `SquareCatalogLink`.
8. **Deleting** an original is blocked while it's referenced.

## Tests

- forged extension rejected
- oversized file rejected
- duplicate rendition run is idempotent
- draft asset not served anonymously
- delete blocked while referenced
- GPS stripped

## Checklist before done

- [ ] Signature validation, limits, checksum, EXIF policy
- [ ] Renditions via the outbox; delivery via the API route only
- [ ] Alt-text gate; delete protection; tests pass
