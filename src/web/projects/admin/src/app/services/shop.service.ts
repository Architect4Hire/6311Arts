import { HttpClient, HttpContext, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { SUPPRESS_PROBLEM_TOAST } from '../core/http/http-context';
import {
  ShopCommand,
  ShopItem,
  ShopItemCreate,
  ShopItemUpdate,
  ShopListQuery,
  ShopPage,
  decodeShopItem,
  decodeShopPage,
} from '../models/shop';

const BASE = '/api/v1/cms/shop';

/**
 * The shop's HTTP (frontend.md: components never inject `HttpClient`). Every refusal is shown in place by the screen
 * that asked — a field error beside its field, a 409 as a comparison — so none is toasted.
 *
 * There is no price or stock write here: those are Square's record, and the commands that request a change arrive
 * in 5.4a and 5.4b (ADR 0032).
 */
@Injectable({ providedIn: 'root' })
export class ShopService {
  private readonly http = inject(HttpClient);

  list(query: ShopListQuery = {}): Observable<ShopPage> {
    let params = new HttpParams();
    if (query.cursor) params = params.set('cursor', query.cursor);
    if (query.limit) params = params.set('limit', query.limit);
    if (query.status) params = params.set('status', query.status);
    if (query.search) params = params.set('search', query.search);
    if (query.category) params = params.set('category', query.category);

    return this.http
      .get<unknown>(BASE, { params, context: quiet() })
      .pipe(map((body) => decoded(decodeShopPage(body), 'shop page')));
  }

  get(id: string): Observable<ShopItem> {
    return this.http
      .get<unknown>(`${BASE}/${encodeURIComponent(id)}`, { context: quiet() })
      .pipe(map((body) => decoded(decodeShopItem(body), 'shop item')));
  }

  create(create: ShopItemCreate): Observable<ShopItem> {
    return this.http
      .post<unknown>(BASE, create, { context: quiet() })
      .pipe(map((body) => decoded(decodeShopItem(body), 'shop item')));
  }

  /** An edit under the row version the editor read. */
  update(id: string, update: ShopItemUpdate, etag: string): Observable<ShopItem> {
    return this.http
      .patch<unknown>(`${BASE}/${encodeURIComponent(id)}`, update, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map((body) => decoded(decodeShopItem(body), 'shop item')));
  }

  /**
   * Publish, unpublish, unschedule or archive (Admins only), under the row version the editor read. `schedule` is
   * not one of these: it is the one command with a body, so it has {@link schedule} of its own (ADR 0036).
   */
  command(
    id: string,
    command: Exclude<ShopCommand, 'schedule'>,
    etag: string,
  ): Observable<ShopItem> {
    return this.http
      .post<unknown>(`${BASE}/${encodeURIComponent(id)}/${command}`, null, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map((body) => decoded(decodeShopItem(body), 'shop item')));
  }

  /**
   * The `schedule` command, under the row version the editor read: the Worker publishes the piece at `scheduledFor`
   * (ADR 0036). Sending it again moves the time.
   */
  schedule(id: string, scheduledFor: string, etag: string): Observable<ShopItem> {
    return this.http
      .post<unknown>(
        `${BASE}/${encodeURIComponent(id)}/schedule`,
        { scheduledFor },
        { headers: new HttpHeaders({ 'If-Match': etag }), context: quiet() },
      )
      .pipe(map((body) => decoded(decodeShopItem(body), 'shop item')));
  }
}

function quiet(): HttpContext {
  return new HttpContext().set(SUPPRESS_PROBLEM_TOAST, true);
}

function decoded<T>(value: T | null, what: string): T {
  if (value === null) throw new Error(`The API returned something that is not a ${what}.`);
  return value;
}
