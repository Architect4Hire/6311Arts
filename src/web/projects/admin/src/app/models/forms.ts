/**
 * The public forms' API shapes as the CMS reads them (ADR 0047), mirroring the API's ServiceModels exactly
 * (`FormsServiceModels.cs`, the OpenAPI snapshot): string ids, enum names as strings, ISO timestamps. Everything a
 * visitor typed is untrusted text: it is shown as text, never as HTML.
 */

import { IconName, StatusTone } from '@6311/ui';
import {
  ApiProblem,
  decodeApiProblem,
  fields,
  isBoolean,
  isNullableString,
  isNumber,
  isString,
  oneOf,
} from './decode';

/** The limits the API enforces (`FormsPolicy`), so a form can stop early and say why. */
export const FORMS_LIMITS = {
  notesMaxLength: 4000,
  /** A newsletter search is part of an address (`FormsPolicy.EmailMaxLength`). */
  searchMaxLength: 254,
} as const;

/** `SubmissionStatus`: where an artist submission is in the inbox. */
export const SUBMISSION_STATUSES = ['New', 'Reviewed', 'Accepted', 'Declined'] as const;
export type SubmissionStatus = (typeof SUBMISSION_STATUSES)[number];

/** How each submission status reads: a word and a glyph, never colour alone (design-system.md). */
export const SUBMISSION_CHIPS: Readonly<
  Record<
    SubmissionStatus,
    { readonly label: string; readonly icon: IconName; readonly tone: StatusTone }
  >
> = {
  New: { label: 'New', icon: 'spark', tone: 'attention' },
  Reviewed: { label: 'Reviewed', icon: 'clock', tone: 'pending' },
  Accepted: { label: 'Accepted', icon: 'check', tone: 'positive' },
  Declined: { label: 'Declined', icon: 'close', tone: 'neutral' },
};

/** Whether a contact message has been dealt with, as a chip. */
export const HANDLED_CHIPS: Readonly<
  Record<
    'handled' | 'unhandled',
    { readonly label: string; readonly icon: IconName; readonly tone: StatusTone }
  >
> = {
  handled: { label: 'Handled', icon: 'check', tone: 'positive' },
  unhandled: { label: 'Unhandled', icon: 'alert', tone: 'attention' },
};

/** `FormsPolicy.Goals`: what an artist said they want, as the words staff read. */
const WANT_LABELS: Readonly<Record<string, string>> = {
  sell: 'Sell',
  exhibit: 'Exhibit',
  teach: 'Teach',
  hello: 'Say hello',
};

/** A want as a word; one this screen does not know yet shows as the API spelled it. */
export function wantLabel(want: string): string {
  return WANT_LABELS[want] ?? want;
}

/** `FormsPolicy.Restock`, as the public form words it. */
const RESTOCK_LABELS: Readonly<Record<string, string>> = {
  yes: 'Yes, regularly',
  limited: 'A few at a time',
  no: 'One-of-a-kind only',
};

export function restockLabel(restock: string): string {
  return RESTOCK_LABELS[restock] ?? restock;
}

/** `NewsletterSource`: where an address joined the list. */
export const NEWSLETTER_SOURCES = ['Signup', 'Submission'] as const;
export type NewsletterSource = (typeof NEWSLETTER_SOURCES)[number];

export const NEWSLETTER_SOURCE_LABELS: Readonly<Record<NewsletterSource, string>> = {
  Signup: 'Sign-up',
  Submission: 'Submission',
};

/** The problem codes the forms screens act on (`ErrorCodes`). */
export const FORMS_CODES = {
  concurrencyConflict: 'concurrency.conflict',
  notFound: 'resource.not_found',
  validationFailed: 'validation.failed',
} as const;

/** `SubmissionSummaryServiceModel`: one row of the inbox. */
export interface SubmissionSummary {
  readonly id: string;
  readonly name: string;
  readonly email: string;
  /** `sell`, `exhibit`, `teach`, `hello`. */
  readonly wants: readonly string[];
  readonly status: SubmissionStatus;
  readonly imageCount: number;
  readonly receivedAt: string;
}

/** `SubmissionSellServiceModel`. */
export interface SubmissionSell {
  readonly piecesReady: number | null;
  /** `yes`, `limited` or `no`. */
  readonly restock: string | null;
}

/** `SubmissionExhibitServiceModel`. */
export interface SubmissionExhibit {
  readonly showTitle: string | null;
  readonly workCount: number | null;
  readonly largest: string | null;
  readonly window: string | null;
  readonly gallery: string | null;
}

/** `SubmissionTeachServiceModel`. */
export interface SubmissionTeach {
  readonly classTitle: string | null;
  readonly maxStudents: number | null;
  readonly format: string | null;
  readonly materials: string | null;
  readonly taughtBefore: boolean | null;
}

/** `SubmissionAnswersServiceModel`: everything else the artist answered. */
export interface SubmissionAnswers {
  readonly pronouns: string | null;
  readonly phone: string | null;
  readonly neighborhood: string | null;
  readonly zip: string | null;
  readonly website: string | null;
  readonly instagram: string | null;
  readonly medium: string | null;
  readonly otherMedia: string | null;
  readonly years: number | null;
  readonly priceBand: string | null;
  readonly bio: string | null;
  readonly statement: string | null;
  readonly sell: SubmissionSell | null;
  readonly exhibit: SubmissionExhibit | null;
  readonly teach: SubmissionTeach | null;
  readonly portfolio: string | null;
  readonly heard: string | null;
  readonly newsletter: boolean;
}

/** `SubmissionImageServiceModel`: a picture sent with a submission, a draft asset only CMS sessions can fetch. */
export interface SubmissionImage {
  readonly id: string;
  readonly fileName: string | null;
  readonly width: number;
  readonly height: number;
  readonly originalUrl: string;
  readonly thumbnailUrl: string | null;
}

/** `SubmissionServiceModel`: one submission in full. */
export interface Submission {
  readonly id: string;
  readonly name: string;
  readonly email: string;
  readonly wants: readonly string[];
  readonly answers: SubmissionAnswers;
  readonly images: readonly SubmissionImage[];
  readonly status: SubmissionStatus;
  readonly notes: string | null;
  readonly receivedAt: string;
  readonly updatedAt: string;
  readonly updatedBy: string | null;
  /** The row version, sent back as `If-Match`. */
  readonly etag: string;
}

/** `SubmissionUpdateViewModel`: a field left out is unchanged; empty notes clear them. */
export interface SubmissionUpdate {
  readonly status?: SubmissionStatus;
  readonly notes?: string;
}

/** `ContactMessageServiceModel`. */
export interface ContactMessage {
  readonly id: string;
  readonly name: string;
  readonly email: string;
  readonly message: string;
  readonly handled: boolean;
  readonly receivedAt: string;
  readonly updatedAt: string;
  readonly updatedBy: string | null;
  readonly etag: string;
}

/** `ContactUpdateViewModel`. */
export interface ContactUpdate {
  readonly handled: boolean;
}

/** `NewsletterSubscriberServiceModel`. */
export interface NewsletterSubscriber {
  readonly id: string;
  readonly email: string;
  readonly consentedAt: string;
  /** Which wording of the consent they agreed to (`FormsPolicy.NewsletterConsentVersion`). */
  readonly consentVersion: string;
  readonly source: NewsletterSource;
  readonly createdAt: string;
}

/** `CursorPage<T>`. */
export interface FormsPage<T> {
  readonly items: readonly T[];
  readonly nextCursor: string | null;
}

export type SubmissionList = FormsPage<SubmissionSummary>;
export type ContactMessageList = FormsPage<ContactMessage>;
export type NewsletterList = FormsPage<NewsletterSubscriber>;

export interface SubmissionListQuery {
  readonly cursor?: string | null;
  readonly limit?: number;
  readonly status?: SubmissionStatus;
}

export interface ContactMessageListQuery {
  readonly cursor?: string | null;
  readonly limit?: number;
  readonly handled?: boolean;
}

export interface NewsletterListQuery {
  readonly cursor?: string | null;
  readonly limit?: number;
  readonly search?: string;
}

/** The newsletter list as a file: what the export answered, and the name the API gave it. */
export interface NewsletterExport {
  readonly blob: Blob;
  readonly fileName: string;
}

const isSubmissionStatus = oneOf(SUBMISSION_STATUSES);
const isNewsletterSource = oneOf(NEWSLETTER_SOURCES);

const isStringArray = (value: unknown): value is string[] =>
  Array.isArray(value) && value.every(isString);

/** An int32: a number or a numeric string, as the other models accept. `undefined` when it is neither. */
function decodeInteger(value: unknown): number | null | undefined {
  if (value === null) return null;
  if (isNumber(value) && Number.isSafeInteger(value)) return value;
  if (typeof value === 'string' && /^-?(?:0|[1-9]\d*)$/.test(value)) {
    const parsed = Number(value);
    return Number.isSafeInteger(parsed) ? parsed : undefined;
  }
  return undefined;
}

function decodePage<T>(
  body: unknown,
  decodeItem: (item: unknown) => T | null,
): FormsPage<T> | null {
  const f = fields(body);
  if (!f || !Array.isArray(f['items']) || !isNullableString(f['nextCursor'])) return null;
  const items = f['items'].map(decodeItem);
  if (items.some((item) => item === null)) return null;
  return { items: items as T[], nextCursor: f['nextCursor'] };
}

export function decodeSubmissionSummary(body: unknown): SubmissionSummary | null {
  const f = fields(body);
  if (!f) return null;
  const imageCount = decodeInteger(f['imageCount']);
  if (
    !isString(f['id']) ||
    !isString(f['name']) ||
    !isString(f['email']) ||
    !isStringArray(f['wants']) ||
    !isSubmissionStatus(f['status']) ||
    imageCount === undefined ||
    imageCount === null ||
    !isString(f['receivedAt'])
  ) {
    return null;
  }

  return {
    id: f['id'],
    name: f['name'],
    email: f['email'],
    wants: [...f['wants']],
    status: f['status'],
    imageCount,
    receivedAt: f['receivedAt'],
  };
}

function decodeSell(body: unknown): SubmissionSell | null | undefined {
  if (body === null) return null;
  const f = fields(body);
  if (!f) return undefined;
  const piecesReady = decodeInteger(f['piecesReady']);
  if (piecesReady === undefined || !isNullableString(f['restock'])) return undefined;
  return { piecesReady, restock: f['restock'] };
}

function decodeExhibit(body: unknown): SubmissionExhibit | null | undefined {
  if (body === null) return null;
  const f = fields(body);
  if (!f) return undefined;
  const workCount = decodeInteger(f['workCount']);
  if (
    workCount === undefined ||
    !isNullableString(f['showTitle']) ||
    !isNullableString(f['largest']) ||
    !isNullableString(f['window']) ||
    !isNullableString(f['gallery'])
  ) {
    return undefined;
  }
  return {
    showTitle: f['showTitle'],
    workCount,
    largest: f['largest'],
    window: f['window'],
    gallery: f['gallery'],
  };
}

function decodeTeach(body: unknown): SubmissionTeach | null | undefined {
  if (body === null) return null;
  const f = fields(body);
  if (!f) return undefined;
  const maxStudents = decodeInteger(f['maxStudents']);
  const taughtBefore = f['taughtBefore'];
  if (
    maxStudents === undefined ||
    !isNullableString(f['classTitle']) ||
    !isNullableString(f['format']) ||
    !isNullableString(f['materials']) ||
    !(taughtBefore === null || isBoolean(taughtBefore))
  ) {
    return undefined;
  }
  return {
    classTitle: f['classTitle'],
    maxStudents,
    format: f['format'],
    materials: f['materials'],
    taughtBefore,
  };
}

const ANSWER_STRINGS = [
  'pronouns',
  'phone',
  'neighborhood',
  'zip',
  'website',
  'instagram',
  'medium',
  'otherMedia',
  'priceBand',
  'bio',
  'statement',
  'portfolio',
  'heard',
] as const;

export function decodeSubmissionAnswers(body: unknown): SubmissionAnswers | null {
  const f = fields(body);
  if (!f) return null;
  if (ANSWER_STRINGS.some((key) => !isNullableString(f[key]))) return null;
  const years = decodeInteger(f['years']);
  const sell = decodeSell(f['sell']);
  const exhibit = decodeExhibit(f['exhibit']);
  const teach = decodeTeach(f['teach']);
  if (
    years === undefined ||
    sell === undefined ||
    exhibit === undefined ||
    teach === undefined ||
    !isBoolean(f['newsletter'])
  ) {
    return null;
  }

  const text = (key: (typeof ANSWER_STRINGS)[number]) => f[key] as string | null;
  return {
    pronouns: text('pronouns'),
    phone: text('phone'),
    neighborhood: text('neighborhood'),
    zip: text('zip'),
    website: text('website'),
    instagram: text('instagram'),
    medium: text('medium'),
    otherMedia: text('otherMedia'),
    years,
    priceBand: text('priceBand'),
    bio: text('bio'),
    statement: text('statement'),
    sell,
    exhibit,
    teach,
    portfolio: text('portfolio'),
    heard: text('heard'),
    newsletter: f['newsletter'],
  };
}

export function decodeSubmissionImage(body: unknown): SubmissionImage | null {
  const f = fields(body);
  if (!f) return null;
  const width = decodeInteger(f['width']);
  const height = decodeInteger(f['height']);
  if (
    !isString(f['id']) ||
    !isNullableString(f['fileName']) ||
    width === undefined ||
    width === null ||
    height === undefined ||
    height === null ||
    !isString(f['originalUrl']) ||
    !isNullableString(f['thumbnailUrl'])
  ) {
    return null;
  }
  return {
    id: f['id'],
    fileName: f['fileName'],
    width,
    height,
    originalUrl: f['originalUrl'],
    thumbnailUrl: f['thumbnailUrl'],
  };
}

export function decodeSubmission(body: unknown): Submission | null {
  const f = fields(body);
  if (!f || !Array.isArray(f['images'])) return null;
  const answers = decodeSubmissionAnswers(f['answers']);
  const images = f['images'].map(decodeSubmissionImage);
  if (
    !isString(f['id']) ||
    !isString(f['name']) ||
    !isString(f['email']) ||
    !isStringArray(f['wants']) ||
    answers === null ||
    images.some((image) => image === null) ||
    !isSubmissionStatus(f['status']) ||
    !isNullableString(f['notes']) ||
    !isString(f['receivedAt']) ||
    !isString(f['updatedAt']) ||
    !isNullableString(f['updatedBy']) ||
    !isString(f['etag'])
  ) {
    return null;
  }

  return {
    id: f['id'],
    name: f['name'],
    email: f['email'],
    wants: [...f['wants']],
    answers,
    images: images as SubmissionImage[],
    status: f['status'],
    notes: f['notes'],
    receivedAt: f['receivedAt'],
    updatedAt: f['updatedAt'],
    updatedBy: f['updatedBy'],
    etag: f['etag'],
  };
}

export function decodeContactMessage(body: unknown): ContactMessage | null {
  const f = fields(body);
  if (
    !f ||
    !isString(f['id']) ||
    !isString(f['name']) ||
    !isString(f['email']) ||
    !isString(f['message']) ||
    !isBoolean(f['handled']) ||
    !isString(f['receivedAt']) ||
    !isString(f['updatedAt']) ||
    !isNullableString(f['updatedBy']) ||
    !isString(f['etag'])
  ) {
    return null;
  }
  return {
    id: f['id'],
    name: f['name'],
    email: f['email'],
    message: f['message'],
    handled: f['handled'],
    receivedAt: f['receivedAt'],
    updatedAt: f['updatedAt'],
    updatedBy: f['updatedBy'],
    etag: f['etag'],
  };
}

export function decodeNewsletterSubscriber(body: unknown): NewsletterSubscriber | null {
  const f = fields(body);
  if (
    !f ||
    !isString(f['id']) ||
    !isString(f['email']) ||
    !isString(f['consentedAt']) ||
    !isString(f['consentVersion']) ||
    !isNewsletterSource(f['source']) ||
    !isString(f['createdAt'])
  ) {
    return null;
  }
  return {
    id: f['id'],
    email: f['email'],
    consentedAt: f['consentedAt'],
    consentVersion: f['consentVersion'],
    source: f['source'],
    createdAt: f['createdAt'],
  };
}

export const decodeSubmissionList = (body: unknown): SubmissionList | null =>
  decodePage(body, decodeSubmissionSummary);

export const decodeContactMessageList = (body: unknown): ContactMessageList | null =>
  decodePage(body, decodeContactMessage);

export const decodeNewsletterList = (body: unknown): NewsletterList | null =>
  decodePage(body, decodeNewsletterSubscriber);

/**
 * A visitor-typed web address as a link target: only an absolute `http(s)` URL is one. Anything else — a bare
 * domain, `javascript:`, a relative path — is shown as text, never followed.
 */
export function safeWebLink(value: string | null): string | null {
  if (!value) return null;
  try {
    const url = new URL(value.trim());
    return url.protocol === 'https:' || url.protocol === 'http:' ? url.href : null;
  } catch {
    return null;
  }
}

/** The file name the API gave a download (`Content-Disposition`), or `null` when it gave none. */
export function fileNameOf(contentDisposition: string | null): string | null {
  if (!contentDisposition) return null;
  const encoded = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(contentDisposition);
  if (encoded) {
    try {
      return sanitizeFileName(decodeURIComponent(encoded[1].trim()));
    } catch {
      // Fall back to the plain parameter.
    }
  }
  const plain = /filename\s*=\s*"?([^";]+)"?/i.exec(contentDisposition);
  return plain ? sanitizeFileName(plain[1].trim()) : null;
}

function sanitizeFileName(name: string): string | null {
  const cleaned = name.replace(/[\\/:*?"<>|\u0000-\u001f]/g, '').trim();
  return cleaned === '' ? null : cleaned;
}

export type FormsProblem = ApiProblem;

export const decodeFormsProblem = decodeApiProblem;
