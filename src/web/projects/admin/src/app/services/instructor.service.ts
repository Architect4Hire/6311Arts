import { HttpClient, HttpContext, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { SUPPRESS_PROBLEM_TOAST } from '../core/http/http-context';
import {
  Instructor,
  InstructorCommand,
  InstructorCreate,
  InstructorListQuery,
  InstructorPage,
  InstructorUpdate,
  decodeInstructor,
  decodeInstructorPage,
} from '../models/instructor';

const BASE = '/api/v1/cms/instructors';

/**
 * The instructors' HTTP (frontend.md: components never inject `HttpClient`). Every refusal is shown in place by
 * the screen that asked — a field error beside its field, a 409 as a comparison — so none is toasted.
 */
@Injectable({ providedIn: 'root' })
export class InstructorService {
  private readonly http = inject(HttpClient);

  list(query: InstructorListQuery = {}): Observable<InstructorPage> {
    let params = new HttpParams();
    if (query.cursor) params = params.set('cursor', query.cursor);
    if (query.limit) params = params.set('limit', query.limit);
    if (query.status) params = params.set('status', query.status);
    if (query.search) params = params.set('search', query.search);

    return this.http
      .get<unknown>(BASE, { params, context: quiet() })
      .pipe(map((body) => decoded(decodeInstructorPage(body), 'instructor page')));
  }

  get(id: string): Observable<Instructor> {
    return this.http
      .get<unknown>(`${BASE}/${encodeURIComponent(id)}`, { context: quiet() })
      .pipe(map((body) => decoded(decodeInstructor(body), 'instructor')));
  }

  create(create: InstructorCreate): Observable<Instructor> {
    return this.http
      .post<unknown>(BASE, create, { context: quiet() })
      .pipe(map((body) => decoded(decodeInstructor(body), 'instructor')));
  }

  /** An edit under the row version the editor read. */
  update(id: string, update: InstructorUpdate, etag: string): Observable<Instructor> {
    return this.http
      .patch<unknown>(`${BASE}/${encodeURIComponent(id)}`, update, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map((body) => decoded(decodeInstructor(body), 'instructor')));
  }

  /** Publish, unpublish or archive (Admins only), under the row version the editor read. */
  command(id: string, command: InstructorCommand, etag: string): Observable<Instructor> {
    return this.http
      .post<unknown>(`${BASE}/${encodeURIComponent(id)}/${command}`, null, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map((body) => decoded(decodeInstructor(body), 'instructor')));
  }

  /**
   * The `schedule` command, under the row version the editor read: the Worker publishes the instructor at
   * `scheduledFor` (ADR 0036). Sending it again moves the time.
   */
  schedule(id: string, scheduledFor: string, etag: string): Observable<Instructor> {
    return this.http
      .post<unknown>(
        `${BASE}/${encodeURIComponent(id)}/schedule`,
        { scheduledFor },
        { headers: new HttpHeaders({ 'If-Match': etag }), context: quiet() },
      )
      .pipe(map((body) => decoded(decodeInstructor(body), 'instructor')));
  }
}

function quiet(): HttpContext {
  return new HttpContext().set(SUPPRESS_PROBLEM_TOAST, true);
}

function decoded<T>(value: T | null, what: string): T {
  if (value === null) throw new Error(`The API returned something that is not a ${what}.`);
  return value;
}
