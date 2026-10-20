import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { SUPPRESS_PROBLEM_TOAST } from '../core/http/http-context';
import { PreviewLink, PreviewType, decodePreviewLink } from '../models/content';

const BASE = '/api/v1/cms/preview';

/**
 * Preview links for unpublished records (ADR 0036). One route for every content type, because one token resolves
 * to any of them.
 */
@Injectable({ providedIn: 'root' })
export class PreviewService {
  private readonly http = inject(HttpClient);

  /**
   * A signed link that works for half an hour. The token is the whole credential, so a caller shows it and
   * forgets it: it is never put in browser storage (auth.md "Token storage").
   */
  link(entityType: PreviewType, entityId: string): Observable<PreviewLink> {
    return this.http
      .post<unknown>(
        BASE,
        { entityType, entityId },
        { context: new HttpContext().set(SUPPRESS_PROBLEM_TOAST, true) },
      )
      .pipe(
        map((body) => {
          const link = decodePreviewLink(body);
          if (link === null)
            throw new Error('The API returned something that is not a preview link.');
          return link;
        }),
      );
  }
}
