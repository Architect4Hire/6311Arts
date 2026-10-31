import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ContentCommand, DeletedList, PublishStatus, decodeDeletedList } from '../models/content';
import { CursorPage } from '../models/decode';
import { decoded, quiet } from './api';

/** What every content list request may carry; a type's own query adds its filters. */
export interface ContentQuery {
  readonly cursor?: string | null;
  readonly limit?: number;
  /** A column the list sorts by, `-` before it for descending (ADR 0062). Absent: the list's own order. */
  readonly sort?: string;
  readonly status?: PublishStatus;
  readonly search?: string;
}

/** The Deleted view's request: its own cursor, not the list's (ADR 0052). */
export interface DeletedQuery {
  readonly cursor?: string | null;
  readonly limit?: number;
  readonly search?: string;
}

/** What one content type's endpoints are; everything else about calling them is the same for every type. */
export interface ContentApiConfig<T, S> {
  /** `/api/v1/cms/artists`. */
  readonly base: string;
  /** How a decode failure names one record: "artist". */
  readonly noun: string;
  readonly plural: string;
  /** What names its Deleted rows: their name or their title. */
  readonly field: 'name' | 'title';
  readonly decode: (body: unknown) => T | null;
  readonly decodePage: (body: unknown) => CursorPage<S> | null;
}

/**
 * The HTTP every publishable type shares (frontend.md: components never inject `HttpClient`): the list, one record,
 * create and edit, the status commands, and the Deleted view with delete and restore (content.md, ADR 0036, 0052).
 * Every write names the row version the editor read; every refusal is shown in place by the screen that asked.
 */
export abstract class ContentApi<
  T,
  S,
  C,
  U,
  Q extends ContentQuery,
  K extends 'name' | 'title' = 'name' | 'title',
> {
  protected readonly http = inject(HttpClient);

  constructor(protected readonly api: ContentApiConfig<T, S>) {}

  list(query: Q = {} as Q): Observable<CursorPage<S>> {
    return this.http
      .get<unknown>(this.api.base, { params: paramsOf(query), context: quiet() })
      .pipe(map((body) => decoded(this.api.decodePage(body), `${this.api.noun} page`)));
  }

  get(id: string): Observable<T> {
    return this.read(this.http.get<unknown>(this.at(id), { context: quiet() }));
  }

  create(create: C): Observable<T> {
    return this.read(this.http.post<unknown>(this.api.base, create, { context: quiet() }));
  }

  /** An edit under the row version the editor read. */
  update(id: string, update: U, etag: string): Observable<T> {
    return this.read(this.http.patch<unknown>(this.at(id), update, this.under(etag)));
  }

  /** Publish, unpublish, unschedule or archive (Admins only), under the row version the editor read. */
  command(id: string, command: Exclude<ContentCommand, 'schedule'>, etag: string): Observable<T> {
    return this.read(this.http.post<unknown>(`${this.at(id)}/${command}`, null, this.under(etag)));
  }

  /**
   * The `schedule` command, under the row version the editor read: the Worker publishes the record at
   * `scheduledFor` (ADR 0036). Sending it again moves the time.
   */
  schedule(id: string, scheduledFor: string, etag: string): Observable<T> {
    return this.read(
      this.http.post<unknown>(`${this.at(id)}/schedule`, { scheduledFor }, this.under(etag)),
    );
  }

  /** The Deleted view (ADR 0052). Admin only; its own cursor, not the list's. */
  listDeleted(query: DeletedQuery = {}): Observable<DeletedList<K>> {
    return this.http
      .get<unknown>(`${this.api.base}/deleted`, { params: paramsOf(query), context: quiet() })
      .pipe(
        map((body) =>
          decoded(
            decodeDeletedList(body, this.api.field as K),
            `list of deleted ${this.api.plural}`,
          ),
        ),
      );
  }

  /**
   * Deletes the record, under the row version the editor read (ADR 0052): it leaves the site and the list, and an
   * Admin can restore it from the Deleted view. Admin only.
   */
  delete(id: string, etag: string): Observable<void> {
    return this.http.delete<void>(this.at(id), this.under(etag));
  }

  /** Brings a deleted record back as a Draft, under the row version the Deleted view read. Admin only. */
  restore(id: string, etag: string): Observable<T> {
    return this.read(this.http.post<unknown>(`${this.at(id)}/restore`, null, this.under(etag)));
  }

  protected at(id: string): string {
    return `${this.api.base}/${encodeURIComponent(id)}`;
  }

  protected under(etag: string) {
    return { headers: new HttpHeaders({ 'If-Match': etag }), context: quiet() };
  }

  protected read(request: Observable<unknown>): Observable<T> {
    return request.pipe(map((body) => decoded(this.api.decode(body), this.api.noun)));
  }
}

/** A query's set values as request parameters; an empty, zero or absent one is left off. */
export function paramsOf(query: object): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(query)) {
    if (value) params = params.set(key, value);
  }
  return params;
}
