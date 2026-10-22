import { HttpClient, HttpContext, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { SUPPRESS_PROBLEM_TOAST } from '../core/http/http-context';
import {
  ClassCommand,
  ClassCreate,
  ClassListQuery,
  ClassOffering,
  ClassPage,
  ClassUpdate,
  decodeClassOffering,
  decodeClassPage,
  ClassDeletedList,
  decodeClassDeletedList,
} from '../models/class';

const BASE = '/api/v1/cms/classes';

/**
 * Classes' HTTP (frontend.md: components never inject `HttpClient`). Every refusal is shown in place by the screen
 * that asked — a field error beside its field, a 409 as a comparison — so none is toasted.
 *
 * There is no price or seat write here: those are Square's record, and the commands that request a change arrive in
 * 5.4a (ADR 0032).
 */
@Injectable({ providedIn: 'root' })
export class ClassService {
  private readonly http = inject(HttpClient);

  list(query: ClassListQuery = {}): Observable<ClassPage> {
    let params = new HttpParams();
    if (query.cursor) params = params.set('cursor', query.cursor);
    if (query.limit) params = params.set('limit', query.limit);
    if (query.status) params = params.set('status', query.status);
    if (query.search) params = params.set('search', query.search);

    return this.http
      .get<unknown>(BASE, { params, context: quiet() })
      .pipe(map((body) => decoded(decodeClassPage(body), 'class page')));
  }

  get(id: string): Observable<ClassOffering> {
    return this.http
      .get<unknown>(`${BASE}/${encodeURIComponent(id)}`, { context: quiet() })
      .pipe(map((body) => decoded(decodeClassOffering(body), 'class')));
  }

  create(create: ClassCreate): Observable<ClassOffering> {
    return this.http
      .post<unknown>(BASE, create, { context: quiet() })
      .pipe(map((body) => decoded(decodeClassOffering(body), 'class')));
  }

  /** An edit under the row version the editor read. */
  update(id: string, update: ClassUpdate, etag: string): Observable<ClassOffering> {
    return this.http
      .patch<unknown>(`${BASE}/${encodeURIComponent(id)}`, update, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map((body) => decoded(decodeClassOffering(body), 'class')));
  }

  /**
   * Publish, unpublish, unschedule or archive (Admins only), under the row version the editor read. `schedule` is
   * not one of these: it is the one command with a body, so it has {@link schedule} of its own (ADR 0036).
   */
  command(
    id: string,
    command: Exclude<ClassCommand, 'schedule'>,
    etag: string,
  ): Observable<ClassOffering> {
    return this.http
      .post<unknown>(`${BASE}/${encodeURIComponent(id)}/${command}`, null, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map((body) => decoded(decodeClassOffering(body), 'class')));
  }

  /**
   * The `schedule` command, under the row version the editor read: the Worker publishes the class at `scheduledFor`
   * (ADR 0036). Sending it again moves the time.
   */
  schedule(id: string, scheduledFor: string, etag: string): Observable<ClassOffering> {
    return this.http
      .post<unknown>(
        `${BASE}/${encodeURIComponent(id)}/schedule`,
        { scheduledFor },
        { headers: new HttpHeaders({ 'If-Match': etag }), context: quiet() },
      )
      .pipe(map((body) => decoded(decodeClassOffering(body), 'class')));
  }

  /** The Deleted view (ADR 0052). Admin only; its own cursor, not the list's. */
  listDeleted(query: { cursor?: string | null; limit?: number; search?: string } = {}): Observable<ClassDeletedList> {
    let params = new HttpParams();
    if (query.cursor) params = params.set('cursor', query.cursor);
    if (query.limit) params = params.set('limit', query.limit);
    if (query.search) params = params.set('search', query.search);

    return this.http
      .get<unknown>(`${BASE}/deleted`, { params, context: quiet() })
      .pipe(map((body) => decoded(decodeClassDeletedList(body), 'list of deleted classes')));
  }

  /**
   * Deletes the class, under the row version the editor read (ADR 0052): it leaves the site and the list, and
   * an Admin can restore it from the Deleted view. Admin only.
   */
  delete(id: string, etag: string): Observable<void> {
    return this.http.delete<void>(`${BASE}/${encodeURIComponent(id)}`, {
      headers: new HttpHeaders({ 'If-Match': etag }),
      context: quiet(),
    });
  }

  /** Brings a deleted class back as a Draft, under the row version the Deleted view read. Admin only. */
  restore(id: string, etag: string): Observable<ClassOffering> {
    return this.http
      .post<unknown>(`${BASE}/${encodeURIComponent(id)}/restore`, null, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map((body) => decoded(decodeClassOffering(body), 'class')));
  }

}

function quiet(): HttpContext {
  return new HttpContext().set(SUPPRESS_PROBLEM_TOAST, true);
}

function decoded<T>(value: T | null, what: string): T {
  if (value === null) throw new Error(`The API returned something that is not a ${what}.`);
  return value;
}
