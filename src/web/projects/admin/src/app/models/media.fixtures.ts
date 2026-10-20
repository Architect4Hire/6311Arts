import { MediaAsset } from './media';

/** One asset as the API returns it, for specs. */
export const ASSET: MediaAsset = {
  id: '33333333-3333-3333-3333-333333333333',
  fileName: 'gallery.jpg',
  mimeType: 'image/jpeg',
  bytes: 600_000,
  uploadedBytes: 2_400_000,
  width: 2560,
  height: 1707,
  checksum: 'a'.repeat(64),
  processingStatus: 'Ready',
  visibility: 'Draft',
  compressionPreset: 'Balanced',
  altText: null,
  credit: null,
  uploadedBy: 'user-1',
  uploadedAt: '2026-10-20T12:00:00+00:00',
  updatedAt: '2026-10-20T12:00:00+00:00',
  etag: '"0123456789abcdef0123456789abcdef"',
  originalUrl: '/media/33333333-3333-3333-3333-333333333333/original?v=aaaaaaaaaaaa',
  renditions: [
    {
      name: '400.webp',
      mimeType: 'image/webp',
      width: 400,
      height: 267,
      bytes: 20_000,
      url: '/media/x/400.webp?v=b',
    },
    {
      name: '800.webp',
      mimeType: 'image/webp',
      width: 800,
      height: 533,
      bytes: 60_000,
      url: '/media/x/800.webp?v=c',
    },
  ],
};
