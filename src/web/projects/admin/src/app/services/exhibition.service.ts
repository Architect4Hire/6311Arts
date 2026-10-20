import { HttpClient, HttpContext, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { SUPPRESS_PROBLEM_TOAST } from '../core/http/http-context';
import {
  Exhibition,
  ExhibitionCommand,
  ExhibitionCreate,
  ExhibitionListQuery,
  ExhibitionPage,
  ExhibitionUpdate,
  decodeExhibition,
  decodeExhibitionPage,
} from '../models/exhibition';

const BASE = '/api/v1/cms/exhibitions';

/**
 * Exhibitions' HTTP (frontend.md: components never inject `HttpClient`). Every refusal is shown in place by the
 * screen that asked — a field error beside its field, a 409 as a comparison — so none is toasted.
 */
@Injectable({ providedIn: 'root' })
export class ExhibitionService {
  private readonly http = inject(HttpClient);

  list(query: ExhibitionListQuery = {}): Observable<ExhibitionPage> {
    let params = new HttpParams();
    if (query.cursor) params = params.set('cursor', query.cursor);
    if (query.limit) params = params.set('limit', query.limit);
    if (query.status) params = params.set('status', query.status);
    if (query.search) params = params.set('search', query.search);

    return this.http
      .get<unknown>(BASE, { params, context: quiet() })
      .pipe(map((body) => decoded(decodeExhibitionPage(body), 'exhibition page')));
  }

  get(id: string): Observable<Exhibition> {
    return this.http
      .get<unknown>(`${BASE}/${encodeURIComponent(id)}`, { context: quiet() })
      .pipe(map((body) => decoded(decodeExhibition(body), 'exhibition')));
  }

  create(create: ExhibitionCreate): Observable<Exhibition> {
    return this.http
      .post<unknown>(BASE, create, { context: quiet() })
      .pipe(map((body) => decoded(decodeExhibition(body), 'exhibition')));
  }

  /** An edit under the row version the editor read. */
  update(id: string, update: ExhibitionUpdate, etag: string): Observable<Exhibition> {
    return this.http
      .patch<unknown>(`${BASE}/${encodeURIComponent(id)}`, update, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map((body) => decoded(decodeExhibition(body), 'exhibition')));
  }

  /** Publish, unpublish or archive (Admins only), under the row version the editor read. */
  command(id: string, command: ExhibitionCommand, etag: string): Observable<Exhibition> {
    return this.http
      .post<unknown>(`${BASE}/${encodeURIComponent(id)}/${command}`, null, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map((body) => decoded(decodeExhibition(body), 'exhibition')));
  }

  /**
   * The `schedule` command, under the row version the editor read: the Worker publishes the exhibition at
   * `scheduledFor` (ADR 0036). Sending it again moves the time.
   */
  schedule(id: string, scheduledFor: string, etag: string): Observable<Exhibition> {
    return this.http
      .post<unknown>(
        `${BASE}/${encodeURIComponent(id)}/schedule`,
        { scheduledFor },
        { headers: new HttpHeaders({ 'If-Match': etag }), context: quiet() },
      )
      .pipe(map((body) => decoded(decodeExhibition(body), 'exhibition')));
  }
}

function quiet(): HttpContext {
  return new HttpContext().set(SUPPRESS_PROBLEM_TOAST, true);
}

function decoded<T>(value: T | null, what: string): T {
  if (value === null) throw new Error(`The API returned something that is not an ${what}.`);
  return value;
}
