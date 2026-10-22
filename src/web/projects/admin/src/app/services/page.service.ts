import { HttpClient, HttpContext, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { SUPPRESS_PROBLEM_TOAST } from '../core/http/http-context';
import {
  Page,
  PageCommand,
  PageCreate,
  PageDeletedList,
  PageList,
  PageListQuery,
  PageUpdate,
  decodePage,
  decodePageDeletedList,
  decodePageList,
} from '../models/page';

const BASE = '/api/v1/cms/pages';

/**
 * Pages' HTTP (frontend.md: components never inject `HttpClient`). Every refusal is shown in place by the screen
 * that asked — a field error beside its field, a 409 as a comparison — so none is toasted.
 */
@Injectable({ providedIn: 'root' })
export class PageService {
  private readonly http = inject(HttpClient);

  list(query: PageListQuery = {}): Observable<PageList> {
    let params = new HttpParams();
    if (query.cursor) params = params.set('cursor', query.cursor);
    if (query.limit) params = params.set('limit', query.limit);
    if (query.status) params = params.set('status', query.status);
    if (query.search) params = params.set('search', query.search);

    return this.http
      .get<unknown>(BASE, { params, context: quiet() })
      .pipe(map((body) => decoded(decodePageList(body), 'list of pages')));
  }

  /** The Deleted view (ADR 0052). Admin only; its own cursor, not the list's. */
  listDeleted(query: PageListQuery = {}): Observable<PageDeletedList> {
    let params = new HttpParams();
    if (query.cursor) params = params.set('cursor', query.cursor);
    if (query.limit) params = params.set('limit', query.limit);
    if (query.search) params = params.set('search', query.search);

    return this.http
      .get<unknown>(`${BASE}/deleted`, { params, context: quiet() })
      .pipe(map((body) => decoded(decodePageDeletedList(body), 'list of deleted pages')));
  }

  get(id: string): Observable<Page> {
    return this.http
      .get<unknown>(`${BASE}/${encodeURIComponent(id)}`, { context: quiet() })
      .pipe(map((body) => decoded(decodePage(body), 'page')));
  }

  create(create: PageCreate): Observable<Page> {
    return this.http
      .post<unknown>(BASE, create, { context: quiet() })
      .pipe(map((body) => decoded(decodePage(body), 'page')));
  }

  /** An edit under the row version the editor read. */
  update(id: string, update: PageUpdate, etag: string): Observable<Page> {
    return this.http
      .patch<unknown>(`${BASE}/${encodeURIComponent(id)}`, update, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map((body) => decoded(decodePage(body), 'page')));
  }

  /** Publish, unpublish or archive (Admins only), under the row version the editor read. */
  command(id: string, command: PageCommand, etag: string): Observable<Page> {
    return this.http
      .post<unknown>(`${BASE}/${encodeURIComponent(id)}/${command}`, null, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map((body) => decoded(decodePage(body), 'page')));
  }

  /**
   * Deletes the page, under the row version the editor read (ADR 0052): it leaves the site and the list, and an
   * Admin can restore it from the Deleted view. Admin only.
   */
  delete(id: string, etag: string): Observable<void> {
    return this.http.delete<void>(`${BASE}/${encodeURIComponent(id)}`, {
      headers: new HttpHeaders({ 'If-Match': etag }),
      context: quiet(),
    });
  }

  /** Brings a deleted page back as a Draft, under the row version the Deleted view read. Admin only. */
  restore(id: string, etag: string): Observable<Page> {
    return this.http
      .post<unknown>(`${BASE}/${encodeURIComponent(id)}/restore`, null, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map((body) => decoded(decodePage(body), 'page')));
  }

  /**
   * The `schedule` command, under the row version the editor read: the Worker publishes the page at
   * `scheduledFor` (ADR 0036). Sending it again moves the time.
   */
  schedule(id: string, scheduledFor: string, etag: string): Observable<Page> {
    return this.http
      .post<unknown>(
        `${BASE}/${encodeURIComponent(id)}/schedule`,
        { scheduledFor },
        { headers: new HttpHeaders({ 'If-Match': etag }), context: quiet() },
      )
      .pipe(map((body) => decoded(decodePage(body), 'page')));
  }
}

function quiet(): HttpContext {
  return new HttpContext().set(SUPPRESS_PROBLEM_TOAST, true);
}

function decoded<T>(value: T | null, what: string): T {
  if (value === null) throw new Error(`The API returned something that is not a ${what}.`);
  return value;
}
