import {
  HttpClient,
  HttpEvent,
  HttpEventType,
  HttpHeaders,
  HttpParams,
} from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, filter, map } from 'rxjs';
import {
  CompressionPreset,
  MediaAsset,
  MediaAssetUpdate,
  MediaListQuery,
  MediaPage,
  decodeAsset,
  decodePage,
} from '../models/media';
import { quiet, decoded } from './api';

const BASE = '/api/v1/cms/media';

/** One step of an upload: how far it has got, then the stored asset. */
export type UploadProgress =
  | { readonly kind: 'progress'; readonly loaded: number; readonly total: number | null }
  | { readonly kind: 'done'; readonly asset: MediaAsset };

/** The fields that travel with a file. The file goes last in the body, as the API requires. */
export interface UploadFields {
  readonly altText?: string;
  readonly credit?: string;
  readonly compressionPreset?: CompressionPreset;
}

/** The media library's HTTP (frontend.md: components never inject `HttpClient`). */
@Injectable({ providedIn: 'root' })
export class MediaService {
  private readonly http = inject(HttpClient);

  list(query: MediaListQuery = {}): Observable<MediaPage> {
    let params = new HttpParams();
    if (query.cursor) params = params.set('cursor', query.cursor);
    if (query.limit) params = params.set('limit', query.limit);
    if (query.missingAltText) params = params.set('missingAltText', true);
    if (query.status) params = params.set('status', query.status);

    // A library that fails to load says so in place, so the toast would be a second copy.
    return this.http
      .get<unknown>(BASE, { params, context: quiet() })
      .pipe(map((body) => decoded(decodePage(body), 'media page')));
  }

  get(id: string): Observable<MediaAsset> {
    return this.http
      .get<unknown>(`${BASE}/${encodeURIComponent(id)}`, { context: quiet() })
      .pipe(map((body) => decoded(decodeAsset(body), 'media asset')));
  }

  /**
   * Uploads one file with progress. Requires the XHR backend: `fetch` cannot report upload progress, which is why
   * the CMS does not use `withFetch()` (ADR 0030). Failures are shown per file by the uploader, not toasted.
   */
  upload(file: File, fields: UploadFields = {}): Observable<UploadProgress> {
    const body = new FormData();
    if (fields.altText) body.append('altText', fields.altText);
    if (fields.credit) body.append('credit', fields.credit);
    if (fields.compressionPreset) body.append('compressionPreset', fields.compressionPreset);
    body.append('file', file, file.name);

    return this.http
      .post<unknown>(BASE, body, { observe: 'events', reportProgress: true, context: quiet() })
      .pipe(
        filter(
          (event: HttpEvent<unknown>) =>
            event.type === HttpEventType.UploadProgress || event.type === HttpEventType.Response,
        ),
        map((event): UploadProgress => {
          if (event.type === HttpEventType.UploadProgress) {
            return { kind: 'progress', loaded: event.loaded, total: event.total ?? null };
          }
          if (event.type !== HttpEventType.Response) throw new Error('Unexpected upload event.');
          return { kind: 'done', asset: decoded(decodeAsset(event.body), 'media asset') };
        }),
      );
  }

  /** A staff edit under the row version the editor read. A 409 or a field error is shown by the editor itself. */
  update(id: string, update: MediaAssetUpdate, etag: string): Observable<MediaAsset> {
    return this.http
      .patch<unknown>(`${BASE}/${encodeURIComponent(id)}`, update, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map((body) => decoded(decodeAsset(body), 'media asset')));
  }

  /** Admin only. `media.in_use` and a 409 are shown by the caller. */
  delete(id: string, etag: string): Observable<void> {
    return this.http
      .delete(`${BASE}/${encodeURIComponent(id)}`, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map(() => undefined));
  }
}
