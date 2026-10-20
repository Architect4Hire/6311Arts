/**
 * The media library's API shapes, mirroring the API's ServiceModels exactly (`MediaServiceModels.cs`, the OpenAPI
 * snapshot): string ids, ISO timestamps, enum names as strings. Bodies are untrusted at the boundary, so every
 * shape has a decoder that returns `null` rather than trusting a cast.
 */

import {
  ApiProblem,
  decodeApiProblem,
  fields,
  isNullableNumber,
  isNullableString,
  isNumber,
  isString,
  oneOf,
} from './decode';

export const PROCESSING_STATUSES = ['Pending', 'Ready', 'Failed'] as const;
export type ProcessingStatus = (typeof PROCESSING_STATUSES)[number];

export const VISIBILITIES = ['Draft', 'Published'] as const;
export type Visibility = (typeof VISIBILITIES)[number];

export const COMPRESSION_PRESETS = ['Balanced', 'HighQuality', 'Smallest'] as const;
export type CompressionPreset = (typeof COMPRESSION_PRESETS)[number];

/** What each preset means to the person choosing it. The master upload is never affected. */
export const PRESET_LABELS: Readonly<Record<CompressionPreset, { label: string; hint: string }>> = {
  Balanced: { label: 'Balanced', hint: 'Good quality at a small size. Right for most images.' },
  HighQuality: {
    label: 'High quality',
    hint: 'Larger files with finer detail, for artwork shown big.',
  },
  Smallest: { label: 'Smallest', hint: 'The lightest files, for thumbnails and backgrounds.' },
};

/** The limits the API enforces (`MediaPolicy`), so the uploader can refuse early and say why. */
export const MEDIA_LIMITS = {
  maxUploadBytes: 25 * 1024 * 1024,
  altTextMaxLength: 500,
  creditMaxLength: 200,
  accept: 'image/jpeg,image/png,image/webp,.jpg,.jpeg,.png,.webp',
  types: ['image/jpeg', 'image/png', 'image/webp'],
} as const;

/** The media problem codes the screens act on (`MediaErrorCodes`, `ErrorCodes`). */
export const MEDIA_CODES = {
  typeUnsupported: 'media.type_unsupported',
  extensionMismatch: 'media.extension_mismatch',
  tooLarge: 'media.too_large',
  /** The edge's own limit, for a body over 26 MiB: the same answer to the person as `tooLarge`. */
  payloadTooLarge: 'request.payload_too_large',
  dimensionsOutOfRange: 'media.dimensions_out_of_range',
  undecodable: 'media.undecodable',
  duplicate: 'media.duplicate',
  inUse: 'media.in_use',
  concurrencyConflict: 'concurrency.conflict',
} as const;

export interface MediaRendition {
  /** For example `800.webp`. */
  readonly name: string;
  readonly mimeType: string;
  readonly width: number;
  readonly height: number;
  readonly bytes: number;
  /** Versioned (`?v=`), relative to the gateway. */
  readonly url: string;
}

export interface MediaAsset {
  readonly id: string;
  readonly fileName: string | null;
  readonly mimeType: string;
  /** The stored, compressed master. */
  readonly bytes: number;
  /** What was uploaded, before compression. */
  readonly uploadedBytes: number | null;
  readonly width: number;
  readonly height: number;
  readonly checksum: string;
  readonly processingStatus: ProcessingStatus;
  readonly visibility: Visibility;
  readonly compressionPreset: CompressionPreset;
  readonly altText: string | null;
  readonly credit: string | null;
  readonly uploadedBy: string;
  readonly uploadedAt: string;
  readonly updatedAt: string;
  /** The row version; sent back as `If-Match`. */
  readonly etag: string;
  readonly originalUrl: string;
  readonly renditions: readonly MediaRendition[];
}

export interface MediaAssetSummary {
  readonly id: string;
  readonly fileName: string | null;
  readonly mimeType: string;
  readonly bytes: number;
  readonly uploadedBytes: number | null;
  readonly width: number;
  readonly height: number;
  readonly processingStatus: ProcessingStatus;
  readonly visibility: Visibility;
  readonly altText: string | null;
  readonly credit: string | null;
  readonly uploadedAt: string;
  /** The 400-wide WebP, or `null` until renditions exist. */
  readonly thumbnailUrl: string | null;
}

export interface MediaPage {
  readonly items: readonly MediaAssetSummary[];
  readonly nextCursor: string | null;
}

/** A `PATCH` body: a field left out is unchanged; an empty string clears it. */
export interface MediaAssetUpdate {
  readonly altText?: string | null;
  readonly credit?: string | null;
  readonly compressionPreset?: CompressionPreset | null;
}

export interface MediaListQuery {
  readonly cursor?: string | null;
  readonly limit?: number;
  readonly missingAltText?: boolean;
  readonly status?: ProcessingStatus;
}

export function decodeRendition(body: unknown): MediaRendition | null {
  const f = fields(body);
  if (!f) return null;
  const { name, mimeType, width, height, bytes, url } = f;
  if (!isString(name) || !isString(mimeType) || !isString(url)) return null;
  if (!isNumber(width) || !isNumber(height) || !isNumber(bytes)) return null;
  return { name, mimeType, width, height, bytes, url };
}

export function decodeAsset(body: unknown): MediaAsset | null {
  const f = fields(body);
  if (!f) return null;
  const renditions = Array.isArray(f['renditions']) ? f['renditions'].map(decodeRendition) : null;
  if (!renditions || renditions.some((rendition) => rendition === null)) return null;
  if (
    !isString(f['id']) ||
    !isNullableString(f['fileName']) ||
    !isString(f['mimeType']) ||
    !isNumber(f['bytes']) ||
    !isNullableNumber(f['uploadedBytes']) ||
    !isNumber(f['width']) ||
    !isNumber(f['height']) ||
    !isString(f['checksum']) ||
    !oneOf(PROCESSING_STATUSES)(f['processingStatus']) ||
    !oneOf(VISIBILITIES)(f['visibility']) ||
    !oneOf(COMPRESSION_PRESETS)(f['compressionPreset']) ||
    !isNullableString(f['altText']) ||
    !isNullableString(f['credit']) ||
    !isString(f['uploadedBy']) ||
    !isString(f['uploadedAt']) ||
    !isString(f['updatedAt']) ||
    !isString(f['etag']) ||
    !isString(f['originalUrl'])
  ) {
    return null;
  }

  return {
    id: f['id'],
    fileName: f['fileName'],
    mimeType: f['mimeType'],
    bytes: f['bytes'],
    uploadedBytes: f['uploadedBytes'],
    width: f['width'],
    height: f['height'],
    checksum: f['checksum'],
    processingStatus: f['processingStatus'],
    visibility: f['visibility'],
    compressionPreset: f['compressionPreset'],
    altText: f['altText'],
    credit: f['credit'],
    uploadedBy: f['uploadedBy'],
    uploadedAt: f['uploadedAt'],
    updatedAt: f['updatedAt'],
    etag: f['etag'],
    originalUrl: f['originalUrl'],
    renditions: renditions as MediaRendition[],
  };
}

export function decodeSummary(body: unknown): MediaAssetSummary | null {
  const f = fields(body);
  if (!f) return null;
  if (
    !isString(f['id']) ||
    !isNullableString(f['fileName']) ||
    !isString(f['mimeType']) ||
    !isNumber(f['bytes']) ||
    !isNullableNumber(f['uploadedBytes']) ||
    !isNumber(f['width']) ||
    !isNumber(f['height']) ||
    !oneOf(PROCESSING_STATUSES)(f['processingStatus']) ||
    !oneOf(VISIBILITIES)(f['visibility']) ||
    !isNullableString(f['altText']) ||
    !isNullableString(f['credit']) ||
    !isString(f['uploadedAt']) ||
    !isNullableString(f['thumbnailUrl'])
  ) {
    return null;
  }

  return {
    id: f['id'],
    fileName: f['fileName'],
    mimeType: f['mimeType'],
    bytes: f['bytes'],
    uploadedBytes: f['uploadedBytes'],
    width: f['width'],
    height: f['height'],
    processingStatus: f['processingStatus'],
    visibility: f['visibility'],
    altText: f['altText'],
    credit: f['credit'],
    uploadedAt: f['uploadedAt'],
    thumbnailUrl: f['thumbnailUrl'],
  };
}

export function decodePage(body: unknown): MediaPage | null {
  const f = fields(body);
  if (!f || !Array.isArray(f['items']) || !isNullableString(f['nextCursor'])) return null;
  const items = f['items'].map(decodeSummary);
  if (items.some((item) => item === null)) return null;
  return { items: items as MediaAssetSummary[], nextCursor: f['nextCursor'] };
}

/** "1.2 MB", "840 KB": sizes as people read them. */
export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** The share saved by compression, as a whole percentage, or `null` when nothing was saved. */
export function savingPercent(uploaded: number | null, stored: number): number | null {
  if (uploaded === null || uploaded <= 0 || stored >= uploaded) return null;
  return Math.round((1 - stored / uploaded) * 100);
}

/** What a refused media request said: its stable code, its message, and the duplicate's id when there is one. */
export interface MediaProblem extends ApiProblem {
  readonly existingAssetId: string | null;
}

/** Decodes an error body; a body that is not ProblemDetails (a proxy page, a dropped connection) gives a bare status. */
export function decodeMediaProblem(body: unknown, status: number): MediaProblem {
  const f = fields(body);
  return {
    ...decodeApiProblem(body, status),
    existingAssetId: f && isString(f['existingAssetId']) ? f['existingAssetId'] : null,
  };
}
