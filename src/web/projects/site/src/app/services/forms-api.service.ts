import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { postWithAntiforgery, problemErrors } from '../core/bff-problem';

/** The artist submission's answers, in the shape `POST /api/v1/forms/submissions` reads (ADR 0047). */
export interface SubmissionAnswers {
  artist: {
    name: string;
    email: string;
    pronouns?: string;
    phone?: string;
    neighborhood?: string;
    zip?: string;
    website?: string;
    instagram?: string;
  };
  work: {
    medium?: string;
    otherMedia?: string;
    years?: number | null;
    priceBand?: string;
    bio?: string;
    statement?: string;
  };
  wants: string[];
  sell?: { piecesReady?: number | null; restock?: string };
  exhibit?: {
    showTitle?: string;
    workCount?: number | null;
    largest?: string;
    window?: string;
    gallery?: string;
  };
  teach?: {
    classTitle?: string;
    maxStudents?: number | null;
    format?: string;
    materials?: string;
    taughtBefore?: boolean;
  };
  portfolio?: string;
  heard?: string;
  consent: { ownWork: boolean; newsletter: boolean };
  /** The honeypot: a person never fills it. */
  company?: string;
}

/** How a form send ended, in the words a person can act on. */
export type FormOutcome =
  | { ok: true }
  | { ok: false; kind: 'invalid'; message: string }
  | { ok: false; kind: 'busy' }
  | { ok: false; kind: 'failed' };

/** The most images one submission may carry, and the largest each may be (ADR 0047, `FormsPolicy`/`MediaPolicy`). */
export const SUBMISSION_LIMITS = { maxImages: 6, maxImageBytes: 10 * 1024 * 1024 } as const;

/**
 * The site's three public forms (ADR 0047): each posts to `/api/v1/forms/*` through the gateway the way every unsafe
 * request must go, with a fresh antiforgery pair (`postWithAntiforgery`). Every success is a bodiless 202 — a sign-up
 * for an address already on the list included — so there is nothing to read back.
 */
@Injectable({ providedIn: 'root' })
export class FormsApi {
  private readonly http = inject(HttpClient);

  subscribe(email: string, company: string): Promise<FormOutcome> {
    return this.send('/api/v1/forms/newsletter', { email, company }, ['email']);
  }

  contact(message: {
    name: string;
    email: string;
    message: string;
    company: string;
  }): Promise<FormOutcome> {
    return this.send('/api/v1/forms/contact', message, ['name', 'email', 'message']);
  }

  /** The answers as a JSON part named `submission`, then each picture as an `images` part, in that order. */
  submit(answers: SubmissionAnswers, images: readonly File[]): Promise<FormOutcome> {
    const body = new FormData();
    body.append('submission', new Blob([JSON.stringify(answers)], { type: 'application/json' }));
    for (const image of images) body.append('images', image, image.name);
    return this.send('/api/v1/forms/submissions', body, [
      'artist.name',
      'artist.email',
      'wants',
      'consent.ownWork',
      'images',
      'artist.website',
      'portfolio',
    ]);
  }

  private async send(url: string, body: object, fields: readonly string[]): Promise<FormOutcome> {
    try {
      await postWithAntiforgery<unknown>(this.http, url, body);
      return { ok: true };
    } catch (error) {
      return outcomeOf(error, fields);
    }
  }
}

/** A refusal the person can fix (the first field message, or the problem's detail), too many sends, or anything else. */
function outcomeOf(error: unknown, fields: readonly string[]): FormOutcome {
  if (!(error instanceof HttpErrorResponse)) return { ok: false, kind: 'failed' };
  if (error.status === 429) return { ok: false, kind: 'busy' };
  if (error.status === 400 || error.status === 413 || error.status === 415) {
    for (const field of fields) {
      const [message] = problemErrors(error, field);
      if (message) return { ok: false, kind: 'invalid', message };
    }
    const body: unknown = error.error;
    const detail =
      typeof body === 'object' && body !== null
        ? (body as Record<string, unknown>)['detail']
        : null;
    return {
      ok: false,
      kind: 'invalid',
      message: typeof detail === 'string' ? detail : 'Please check the form and try again.',
    };
  }
  return { ok: false, kind: 'failed' };
}
