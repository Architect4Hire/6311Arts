---
paths:
  - src/Arts6311.Domain/Modules/Media/**
  - src/Arts6311.Api/Controllers/Media*
---
# Media

Bytes live in blob storage (container `media`). Metadata, relationships and processing status live in SQL.

## Asset kinds

| Kind | Meaning |
|---|---|
| `Original` | Staff upload. Immutable. |
| `Imported` | Pulled from WordPress by the importer, with source URL and import date recorded |
| `Derived` | A rendition (400 / 800 / 1600 wide, WebP + JPEG) linked to its original |

## Required metadata

- MIME type (checked from the file signature, not the extension)
- byte size, width, height
- SHA-256 checksum
- kind and parent asset
- processing status
- alt text (required before an asset can be attached to published content)
- credit/attribution
- uploaded by and when

## Access and processing

- **Never public blob access.** Delivery is `/media/{assetId}/{rendition}` through the gateway, with long-lived cache headers and ETags. Draft-only assets are served only to CMS sessions.
- **Uploads:**
  - Images only: JPEG, PNG, WebP. No SVG.
  - Size and dimension limits.
  - Streamed through the gateway.
  - EXIF location data stripped; copyright kept.
- **Renditions** are produced by the `media.rendition-requested` outbox handler in the Worker. They're idempotent (content-addressed by checksum + rendition).
- **Square images.** An image attached to a Square-synced item is uploaded to Square by the catalog push handler. The `SquareCatalogLink` records the Square image id; the asset never stores it.
- **Deleting** an original is blocked while it's referenced by content or by a Square link.
- **Alt text** describes what's in the picture. It's never generated from the filename or title.
