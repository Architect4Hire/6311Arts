import { HttpClient, HttpContext, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { SUPPRESS_PROBLEM_TOAST } from '../core/http/http-context';
import {
  Artist,
  ArtistCommand,
  ArtistCreate,
  ArtistListQuery,
  ArtistPage,
  ArtistUpdate,
  decodeArtist,
  decodeArtistPage,
} from '../models/artist';

const BASE = '/api/v1/cms/artists';

/**
 * The artist roster's HTTP (frontend.md: components never inject `HttpClient`). Every refusal is shown in place by
 * the screen that asked — a field error beside its field, a 409 as a comparison — so none is toasted.
 */
@Injectable({ providedIn: 'root' })
export class ArtistService {
  private readonly http = inject(HttpClient);

  list(query: ArtistListQuery = {}): Observable<ArtistPage> {
    let params = new HttpParams();
    if (query.cursor) params = params.set('cursor', query.cursor);
    if (query.limit) params = params.set('limit', query.limit);
    if (query.status) params = params.set('status', query.status);
    if (query.search) params = params.set('search', query.search);

    return this.http
      .get<unknown>(BASE, { params, context: quiet() })
      .pipe(map((body) => decoded(decodeArtistPage(body), 'artist page')));
  }

  get(id: string): Observable<Artist> {
    return this.http
      .get<unknown>(`${BASE}/${encodeURIComponent(id)}`, { context: quiet() })
      .pipe(map((body) => decoded(decodeArtist(body), 'artist')));
  }

  create(create: ArtistCreate): Observable<Artist> {
    return this.http
      .post<unknown>(BASE, create, { context: quiet() })
      .pipe(map((body) => decoded(decodeArtist(body), 'artist')));
  }

  /** An edit under the row version the editor read. */
  update(id: string, update: ArtistUpdate, etag: string): Observable<Artist> {
    return this.http
      .patch<unknown>(`${BASE}/${encodeURIComponent(id)}`, update, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map((body) => decoded(decodeArtist(body), 'artist')));
  }

  /** Publish, unpublish or archive (Admins only), under the row version the editor read. */
  command(id: string, command: ArtistCommand, etag: string): Observable<Artist> {
    return this.http
      .post<unknown>(`${BASE}/${encodeURIComponent(id)}/${command}`, null, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map((body) => decoded(decodeArtist(body), 'artist')));
  }
}

function quiet(): HttpContext {
  return new HttpContext().set(SUPPRESS_PROBLEM_TOAST, true);
}

function decoded<T>(value: T | null, what: string): T {
  if (value === null) throw new Error(`The API returned something that is not a ${what}.`);
  return value;
}
