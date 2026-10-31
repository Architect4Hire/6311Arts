import { HttpClient, HttpContext, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { SUPPRESS_PROBLEM_TOAST } from '../core/http/http-context';
import {
  MembershipPlan,
  MembershipPlanCreate,
  MembershipPlanList,
  MembershipPlanListQuery,
  MembershipPlanUpdate,
  MembershipPlainCommand,
  decodeMembershipPlan,
  decodeMembershipPlanList,
  MembershipPlanDeletedList,
  decodeMembershipPlanDeletedList,
} from '../models/membership';

const BASE = '/api/v1/cms/memberships';

/**
 * Memberships' HTTP (frontend.md: components never inject `HttpClient`). Every refusal is shown in place by the
 * screen that asked — a field error beside its field, a 409 as a comparison — so none is toasted.
 */
@Injectable({ providedIn: 'root' })
export class MembershipService {
  private readonly http = inject(HttpClient);

  list(query: MembershipPlanListQuery = {}): Observable<MembershipPlanList> {
    let params = new HttpParams();
    if (query.cursor) params = params.set('cursor', query.cursor);
    if (query.limit) params = params.set('limit', query.limit);
    if (query.sort) params = params.set('sort', query.sort);
    if (query.status) params = params.set('status', query.status);
    if (query.search) params = params.set('search', query.search);

    return this.http
      .get<unknown>(BASE, { params, context: quiet() })
      .pipe(map((body) => decoded(decodeMembershipPlanList(body), 'list of membership plans')));
  }

  get(id: string): Observable<MembershipPlan> {
    return this.http
      .get<unknown>(`${BASE}/${encodeURIComponent(id)}`, { context: quiet() })
      .pipe(map((body) => decoded(decodeMembershipPlan(body), 'membership plan')));
  }

  create(create: MembershipPlanCreate): Observable<MembershipPlan> {
    return this.http
      .post<unknown>(BASE, create, { context: quiet() })
      .pipe(map((body) => decoded(decodeMembershipPlan(body), 'membership plan')));
  }

  /** An edit under the row version the editor read. */
  update(id: string, update: MembershipPlanUpdate, etag: string): Observable<MembershipPlan> {
    return this.http
      .patch<unknown>(`${BASE}/${encodeURIComponent(id)}`, update, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map((body) => decoded(decodeMembershipPlan(body), 'membership plan')));
  }

  /**
   * Publish, unpublish, unschedule or archive (Admins only), under the row version the editor read. `schedule`
   * is not among them: it carries a body, so it has {@link MembershipService.schedule} of its own.
   */
  command(id: string, command: MembershipPlainCommand, etag: string): Observable<MembershipPlan> {
    return this.http
      .post<unknown>(`${BASE}/${encodeURIComponent(id)}/${command}`, null, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map((body) => decoded(decodeMembershipPlan(body), 'membership plan')));
  }

  /**
   * The `schedule` command, under the row version the editor read: the Worker publishes the tier at
   * `scheduledFor` (ADR 0036). Sending it again moves the time.
   */
  schedule(id: string, scheduledFor: string, etag: string): Observable<MembershipPlan> {
    return this.http
      .post<unknown>(
        `${BASE}/${encodeURIComponent(id)}/schedule`,
        { scheduledFor },
        { headers: new HttpHeaders({ 'If-Match': etag }), context: quiet() },
      )
      .pipe(map((body) => decoded(decodeMembershipPlan(body), 'membership plan')));
  }

  /** The Deleted view (ADR 0052). Admin only; its own cursor, not the list's. */
  listDeleted(query: { cursor?: string | null; limit?: number; search?: string } = {}): Observable<MembershipPlanDeletedList> {
    let params = new HttpParams();
    if (query.cursor) params = params.set('cursor', query.cursor);
    if (query.limit) params = params.set('limit', query.limit);
    if (query.search) params = params.set('search', query.search);

    return this.http
      .get<unknown>(`${BASE}/deleted`, { params, context: quiet() })
      .pipe(map((body) => decoded(decodeMembershipPlanDeletedList(body), 'list of deleted plans')));
  }

  /**
   * Deletes the plan, under the row version the editor read (ADR 0052): it leaves the site and the list, and
   * an Admin can restore it from the Deleted view. Admin only.
   */
  delete(id: string, etag: string): Observable<void> {
    return this.http.delete<void>(`${BASE}/${encodeURIComponent(id)}`, {
      headers: new HttpHeaders({ 'If-Match': etag }),
      context: quiet(),
    });
  }

  /** Brings a deleted plan back as a Draft, under the row version the Deleted view read. Admin only. */
  restore(id: string, etag: string): Observable<MembershipPlan> {
    return this.http
      .post<unknown>(`${BASE}/${encodeURIComponent(id)}/restore`, null, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map((body) => decoded(decodeMembershipPlan(body), 'plan')));
  }

}

function quiet(): HttpContext {
  return new HttpContext().set(SUPPRESS_PROBLEM_TOAST, true);
}

function decoded<T>(value: T | null, what: string): T {
  if (value === null) throw new Error(`The API returned something that is not a ${what}.`);
  return value;
}
