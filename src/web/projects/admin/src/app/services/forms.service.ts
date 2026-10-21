import {
  HttpClient,
  HttpContext,
  HttpErrorResponse,
  HttpHeaders,
  HttpParams,
} from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, from, map, switchMap, throwError } from 'rxjs';
import { SUPPRESS_PROBLEM_TOAST } from '../core/http/http-context';
import {
  ContactMessage,
  ContactMessageList,
  ContactMessageListQuery,
  ContactUpdate,
  NewsletterExport,
  NewsletterList,
  NewsletterListQuery,
  Submission,
  SubmissionList,
  SubmissionListQuery,
  SubmissionUpdate,
  decodeContactMessage,
  decodeContactMessageList,
  decodeNewsletterList,
  decodeSubmission,
  decodeSubmissionList,
  fileNameOf,
} from '../models/forms';

const BASE = '/api/v1/cms/forms';

/** What the export is saved as when the API names no file. */
const EXPORT_FALLBACK_NAME = '6311arts-newsletter.csv';

/**
 * What visitors sent through the public forms (ADR 0047): the submissions inbox, contact messages and the newsletter
 * list (frontend.md: components never inject `HttpClient`). Every refusal is shown in place by the screen that
 * asked — a 409 as a comparison, a failed load as a notice — so none is toasted.
 */
@Injectable({ providedIn: 'root' })
export class FormsService {
  private readonly http = inject(HttpClient);

  listSubmissions(query: SubmissionListQuery = {}): Observable<SubmissionList> {
    let params = paging(query);
    if (query.status) params = params.set('status', query.status);

    return this.http
      .get<unknown>(`${BASE}/submissions`, { params, context: quiet() })
      .pipe(map((body) => decoded(decodeSubmissionList(body), 'list of submissions')));
  }

  getSubmission(id: string): Observable<Submission> {
    return this.http
      .get<unknown>(`${BASE}/submissions/${encodeURIComponent(id)}`, { context: quiet() })
      .pipe(map((body) => decoded(decodeSubmission(body), 'submission')));
  }

  /** A status move or a notes edit, under the row version the screen read. */
  updateSubmission(id: string, update: SubmissionUpdate, etag: string): Observable<Submission> {
    return this.http
      .patch<unknown>(`${BASE}/submissions/${encodeURIComponent(id)}`, update, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map((body) => decoded(decodeSubmission(body), 'submission')));
  }

  listMessages(query: ContactMessageListQuery = {}): Observable<ContactMessageList> {
    let params = paging(query);
    if (query.handled !== undefined) params = params.set('handled', query.handled);

    return this.http
      .get<unknown>(`${BASE}/messages`, { params, context: quiet() })
      .pipe(map((body) => decoded(decodeContactMessageList(body), 'list of messages')));
  }

  getMessage(id: string): Observable<ContactMessage> {
    return this.http
      .get<unknown>(`${BASE}/messages/${encodeURIComponent(id)}`, { context: quiet() })
      .pipe(map((body) => decoded(decodeContactMessage(body), 'contact message')));
  }

  /** Marks a message handled or not, under the row version the screen read. */
  updateMessage(id: string, update: ContactUpdate, etag: string): Observable<ContactMessage> {
    return this.http
      .patch<unknown>(`${BASE}/messages/${encodeURIComponent(id)}`, update, {
        headers: new HttpHeaders({ 'If-Match': etag }),
        context: quiet(),
      })
      .pipe(map((body) => decoded(decodeContactMessage(body), 'contact message')));
  }

  listSubscribers(query: NewsletterListQuery = {}): Observable<NewsletterList> {
    let params = paging(query);
    if (query.search) params = params.set('search', query.search);

    return this.http
      .get<unknown>(`${BASE}/newsletter`, { params, context: quiet() })
      .pipe(map((body) => decoded(decodeNewsletterList(body), 'newsletter list')));
  }

  /** Takes an address off the list (Admins only, audited). */
  removeSubscriber(id: string): Observable<void> {
    return this.http
      .delete<unknown>(`${BASE}/newsletter/${encodeURIComponent(id)}`, { context: quiet() })
      .pipe(map(() => undefined));
  }

  /** The whole list as a CSV file (Admins only, audited), with the name the API gave it. */
  exportSubscribers(): Observable<NewsletterExport> {
    return this.http
      .get(`${BASE}/newsletter/export`, {
        observe: 'response',
        responseType: 'blob',
        context: quiet(),
      })
      .pipe(
        map((response) => {
          if (response.body === null) throw new Error('The API returned no file.');
          return {
            blob: response.body,
            fileName:
              fileNameOf(response.headers.get('Content-Disposition')) ?? EXPORT_FALLBACK_NAME,
          };
        }),
        // A refusal arrives as a Blob too, since that is what was asked for: read it back into ProblemDetails so
        // the screen can say why, with the correlation id.
        catchError((error: unknown) =>
          from(withJsonBody(error)).pipe(switchMap((readable) => throwError(() => readable))),
        ),
      );
  }
}

function paging(query: { readonly cursor?: string | null; readonly limit?: number }): HttpParams {
  let params = new HttpParams();
  if (query.cursor) params = params.set('cursor', query.cursor);
  if (query.limit) params = params.set('limit', query.limit);
  return params;
}

async function withJsonBody(error: unknown): Promise<unknown> {
  if (!(error instanceof HttpErrorResponse) || !(error.error instanceof Blob)) return error;
  let body: unknown = null;
  try {
    body = JSON.parse(await error.error.text());
  } catch {
    // Not JSON (a proxy page, an empty body): the status alone will have to do.
  }
  return new HttpErrorResponse({
    error: body,
    headers: error.headers,
    status: error.status,
    statusText: error.statusText,
    url: error.url ?? undefined,
  });
}

function quiet(): HttpContext {
  return new HttpContext().set(SUPPRESS_PROBLEM_TOAST, true);
}

function decoded<T>(value: T | null, what: string): T {
  if (value === null) throw new Error(`The API returned something that is not a ${what}.`);
  return value;
}
