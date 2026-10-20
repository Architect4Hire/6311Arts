import { HttpClient, HttpContext, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { SUPPRESS_PROBLEM_TOAST } from '../core/http/http-context';
import {
  Redirect,
  RedirectCreate,
  RedirectList,
  RedirectListQuery,
  RedirectUpdate,
  decodeRedirect,
  decodeRedirectList,
} from '../models/redirect';

const BASE = '/api/v1/cms/redirects';

/**
 * Redirects' HTTP, Admins only (frontend.md: components never inject `HttpClient`). Refusals are shown in place by the
 * screen that asked, so none is toasted.
 */
@Injectable({ providedIn: 'root' })
export class RedirectService {
  private readonly http = inject(HttpClient);

  list(query: RedirectListQuery = {}): Observable<RedirectList> {
    let params = new HttpParams();
    if (query.cursor) params = params.set('cursor', query.cursor);
    if (query.limit) params = params.set('limit', query.limit);
    if (query.source) params = params.set('source', query.source);
    if (query.search) params = params.set('search', query.search);

    return this.http
      .get<unknown>(BASE, { params, context: quiet() })
      .pipe(map((body) => decoded(decodeRedirectList(body), 'list of redirects')));
  }

  get(id: string): Observable<Redirect> {
    return this.http
      .get<unknown>(`${BASE}/${encodeURIComponent(id)}`, { context: quiet() })
      .pipe(map((body) => decoded(decodeRedirect(body), 'redirect')));
  }

  create(create: RedirectCreate): Observable<Redirect> {
    return this.http
      .post<unknown>(BASE, create, { context: quiet() })
      .pipe(map((body) => decoded(decodeRedirect(body), 'redirect')));
  }

  update(id: string, update: RedirectUpdate, etag: string): Observable<Redirect> {
    return this.http
      .patch<unknown>(`${BASE}/${encodeURIComponent(id)}`, update, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map((body) => decoded(decodeRedirect(body), 'redirect')));
  }

  delete(id: string, etag: string): Observable<void> {
    return this.http
      .delete<unknown>(`${BASE}/${encodeURIComponent(id)}`, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map(() => undefined));
  }
}

function quiet(): HttpContext {
  return new HttpContext().set(SUPPRESS_PROBLEM_TOAST, true);
}

function decoded<T>(value: T | null, what: string): T {
  if (value === null) throw new Error(`The API returned something that is not a ${what}.`);
  return value;
}
