/**
 * The shop's API shapes, mirroring the API's ServiceModels exactly (`ShopItemServiceModels.cs`, the OpenAPI
 * snapshot): string ids, UTC ISO instants, integer cents with a currency, enum names as strings.
 *
 * What Square holds the record of — `priceCents`, `stockOnHand` and `soldOut` — appears only on the read models
 * here, never on a write one (square.md, ADR 0032).
 */

import {
  CONTENT_CODES,
  CONTENT_FIELDS,
  ContentCommand,
  ContentRecord,
  Credit,
  credit,
  decodeDeletedList,
  DeletedList,
  DeletedRow,
  PublishStatus,
  SLUG_MAX_LENGTH,
  status,
} from './content';
import {
  ApiProblem,
  CursorPage,
  decodeApiProblem,
  decodeCursorPage,
  decodeShape,
  flag,
  int64,
  listOf,
  nullable,
  optionalText,
  shaped,
  text,
  whole,
} from './decode';

/** The limits the API enforces (`ShopPolicy`, `SkuPolicy`, `Slugs`), so the form can stop early and say why. */
export const SHOP_LIMITS = {
  titleMaxLength: 200,
  descriptionMaxLength: 100_000,
  descriptionInputMaxLength: 200_000,
  slugMaxLength: SLUG_MAX_LENGTH,
  galleryMaxCount: 12,
  startingPriceMaxCents: 5_000_000,
  startingStockMaxQuantity: 1_000,
  skuMaxLength: 64,
  searchMaxLength: 100,
} as const;

/**
 * The categories artwork is filed under, mirroring `ShopPolicy.Categories` (ADR 0038): the slugs are the contract
 * and the labels are ours to show, as `SHOP_LIMITS` mirrors the policy's numbers. The site keeps its own copy.
 */
export const SHOP_CATEGORIES = [
  { slug: 'jewelry', label: 'Jewelry' },
  { slug: 'prints', label: 'Prints' },
  { slug: 'ceramics', label: 'Ceramics' },
  { slug: 'painting', label: 'Painting' },
  { slug: 'sculpture', label: 'Sculpture' },
  { slug: 'books-magazines', label: 'Books and magazines' },
] as const;

export type ShopCategory = (typeof SHOP_CATEGORIES)[number]['slug'];

/** The label for a category slug; the slug itself for anything the API sends that this list does not know. */
export function categoryLabel(slug: string): string {
  return SHOP_CATEGORIES.find((category) => category.slug === slug)?.label ?? slug;
}

/** The problem codes the shop screens act on: the shared content ones plus the catalog's SKU clash (ADR 0037). */
export const SHOP_CODES = {
  ...CONTENT_CODES,
  /** `CatalogErrorCodes.SkuTaken`: another shop item or class session already uses the SKU. */
  skuTaken: 'cms.sku_taken',
} as const;

export type ShopCommand = ContentCommand;

/** The shape the API accepts as a SKU (`SkuPolicy.IsValid`), once normalized. */
export function isSku(value: string): boolean {
  return (
    value.length > 0 &&
    value.length <= SHOP_LIMITS.skuMaxLength &&
    /^[A-Z0-9]+(?:[-.][A-Z0-9]+)*$/.test(value)
  );
}

/** A SKU as the API stores and compares it (`SkuPolicy.Normalize`): trimmed and upper-cased. */
export function normalizeSku(value: string): string {
  return value.trim().toUpperCase();
}

/** The artist a piece credits, as the Artists module describes them now. */
export type ShopItemArtist = Credit;

export interface ShopItem extends ContentRecord {
  readonly title: string;
  /** Sanitized HTML, exactly as stored. */
  readonly description: string | null;
  readonly artist: ShopItemArtist | null;
  readonly categories: readonly string[];
  /** The photographs, in order. The first is the lead (ADR 0038). */
  readonly imageMediaIds: readonly string[];
  /** The library images the description shows. */
  readonly descriptionImageMediaIds: readonly string[];
  readonly sku: string;
  /** The number its suggested SKU is built from. */
  readonly itemNumber: number;
  readonly oneOfAKind: boolean;
  readonly startingPriceCents: number | null;
  readonly startingStockQuantity: number | null;
  readonly currency: string;
  /** `false` once Square has reported money or stock: from then on a change is its own command (5.4a). */
  readonly startingPriceEditable: boolean;
  /** The same gate for the one-of-a-kind flag and the starting count (5.4b, ADR 0038). */
  readonly startingStockEditable: boolean;
  /** Square's record of the price; `null` until Square has reported one. */
  readonly priceCents: number | null;
  /** Square's record of the stock; `null` until the first pull. */
  readonly stockOnHand: number | null;
  readonly soldOut: boolean;
  /** Square has reported money or stock for this piece (ADR 0037, 0038). */
  readonly squareHasRecord: boolean;
  /** It has been published, so changing its web address redirects the old one (ADR 0035). */
  readonly slugLocked: boolean;
}

export interface ShopItemSummary {
  readonly id: string;
  readonly title: string;
  readonly slug: string;
  readonly status: PublishStatus;
  readonly artist: ShopItemArtist | null;
  readonly categories: readonly string[];
  readonly leadImageMediaId: string | null;
  readonly sku: string;
  readonly oneOfAKind: boolean;
  readonly priceCents: number | null;
  readonly stockOnHand: number | null;
  readonly soldOut: boolean;
  /** The currency `priceCents` is in (api-contract.md). */
  readonly currency: string;
  readonly updatedAt: string;
}

export type ShopPage = CursorPage<ShopItemSummary>;

/** A `POST` body. A slug left out is made from the title, and a SKU left out is suggested as `ART-<item>`. */
export interface ShopItemCreate {
  readonly title: string;
  readonly slug?: string;
  readonly description?: string;
  readonly artistId?: string;
  readonly categories?: readonly string[];
  readonly imageMediaIds?: readonly string[];
  readonly sku?: string;
  readonly oneOfAKind?: boolean;
  readonly startingPriceCents?: number;
  readonly startingStockQuantity?: number;
}

/**
 * A `PATCH` body: a field left out is unchanged; an empty string clears the description; `categories` and
 * `imageMediaIds` replace their sets.
 */
export interface ShopItemUpdate {
  readonly title?: string;
  readonly slug?: string;
  readonly description?: string;
  readonly artistId?: string;
  readonly removeArtist?: true;
  readonly categories?: readonly string[];
  readonly imageMediaIds?: readonly string[];
  readonly sku?: string;
  readonly oneOfAKind?: boolean;
  readonly startingPriceCents?: number;
  readonly removeStartingPrice?: true;
  readonly startingStockQuantity?: number;
}

export interface ShopListQuery {
  readonly cursor?: string | null;
  readonly limit?: number;
  /** A column the list sorts by, `-` before it for descending (ADR 0062). Absent: the list's own order. */
  readonly sort?: string;
  readonly status?: PublishStatus;
  readonly search?: string;
  readonly category?: string;
}

export function decodeShopItem(body: unknown): ShopItem | null {
  return decodeShape(body, {
    ...CONTENT_FIELDS,
    title: text,
    description: optionalText,
    artist: nullable(credit),
    categories: listOf(text),
    imageMediaIds: listOf(text),
    descriptionImageMediaIds: listOf(text),
    sku: text,
    itemNumber: whole,
    oneOfAKind: flag,
    startingPriceCents: int64,
    startingStockQuantity: int64,
    currency: text,
    startingPriceEditable: flag,
    startingStockEditable: flag,
    priceCents: int64,
    stockOnHand: int64,
    soldOut: flag,
    squareHasRecord: flag,
    slugLocked: flag,
  });
}

export function decodeShopItemSummary(body: unknown): ShopItemSummary | null {
  return decodeShape(body, {
    id: text,
    title: text,
    slug: text,
    status,
    artist: nullable(credit),
    categories: listOf(text),
    leadImageMediaId: optionalText,
    sku: text,
    oneOfAKind: flag,
    priceCents: int64,
    stockOnHand: int64,
    soldOut: flag,
    currency: text,
    updatedAt: text,
  });
}

export function decodeShopPage(body: unknown): ShopPage | null {
  return decodeCursorPage(body, decodeShopItemSummary);
}

/**
 * The CSV import's limits (`ShopPolicy.ImportMaxRows`, `ImportMaxCharacters`), so the page can refuse a file before
 * sending it and say why.
 */
export const SHOP_IMPORT_LIMITS = {
  maxRows: 1_000,
  maxCharacters: 2_000_000,
  fileNameMaxLength: 255,
} as const;

/**
 * The file's name as the API accepts it for the audit trail: no control characters, at most
 * {@link SHOP_IMPORT_LIMITS}`.fileNameMaxLength` UTF-16 units (as .NET counts them), and `null` when nothing is left.
 * A name is a convenience, so it is tidied rather than refused.
 */
export function shopImportFileName(name: string): string | null {
  let tidy = name.replace(/[\u0000-\u001F\u007F-\u009F]/g, '').trim();
  if (tidy.length > SHOP_IMPORT_LIMITS.fileNameMaxLength) {
    tidy = tidy.slice(0, SHOP_IMPORT_LIMITS.fileNameMaxLength);
    // Never end on half of a surrogate pair, which would be no character at all.
    if (/[\uD800-\uDBFF]$/.test(tidy)) tidy = tidy.slice(0, -1);
  }
  return tidy === '' ? null : tidy;
}

/** The column reference the import page shows, in the template's order. Only `title` is required. */
export const SHOP_IMPORT_COLUMNS = [
  { name: 'title', required: true, rule: 'The piece’s title, up to 200 characters.' },
  {
    name: 'sku',
    required: false,
    rule: 'Letters, digits, hyphens and dots, up to 64 characters. Leave blank for a suggested ART-number.',
  },
  {
    name: 'artist',
    required: false,
    rule: 'The artist’s slug or their exact name, as on the roster. Leave blank to credit nobody.',
  },
  {
    name: 'categories',
    required: false,
    rule: `Separated by “|”, from: ${SHOP_CATEGORIES.map((category) => category.slug).join(', ')}.`,
  },
  {
    name: 'one_of_a_kind',
    required: false,
    rule: 'true/false, yes/no (or y/n, 1/0). Blank means no.',
  },
  {
    name: 'starting_price',
    required: false,
    rule: 'In dollars, such as 45 or 45.00, up to $50,000.',
  },
  {
    name: 'starting_stock',
    required: false,
    rule: 'A whole number from 1 to 1,000. Blank or 1 for a one-of-a-kind piece.',
  },
  {
    name: 'slug',
    required: false,
    rule: 'The web address. Leave blank to make one from the title; a taken one gets -2, -3 added.',
  },
  {
    name: 'description',
    required: false,
    rule: 'Plain text. A blank line starts a new paragraph.',
  },
] as const;

/**
 * The template's example rows, in {@link SHOP_IMPORT_COLUMNS} order. They credit nobody, so the untouched template
 * previews cleanly on any roster; the column reference explains the artist column.
 */
const SHOP_IMPORT_EXAMPLES: readonly (readonly string[])[] = [
  [
    'Blue Heron',
    '',
    '',
    'prints',
    'false',
    '45.00',
    '10',
    '',
    'Giclée print on archival paper.\n\nSigned and numbered.',
  ],
  [
    'Raku Bowl',
    'CER-001',
    '',
    'ceramics',
    'true',
    '180',
    '1',
    'raku-bowl',
    'Hand-thrown raku-fired bowl.',
  ],
];

/** One RFC 4180 field: quoted only when it holds a comma, a quote or a line break. */
function csvField(value: string): string {
  return /[",\r\n]/.test(value) ? `"${value.replace(/"/g, '""')}"` : value;
}

/** RFC 4180 rows, CRLF line endings. */
export function toCsv(rows: readonly (readonly string[])[]): string {
  return rows.map((row) => row.map(csvField).join(',')).join('\r\n') + '\r\n';
}

/**
 * The downloadable template: the header and two example rows, behind a UTF-8 byte-order mark so Excel opens it
 * as UTF-8 rather than guessing the system code page.
 */
export function shopImportTemplate(): string {
  return (
    '\uFEFF' + toCsv([SHOP_IMPORT_COLUMNS.map((column) => column.name), ...SHOP_IMPORT_EXAMPLES])
  );
}

export const SHOP_IMPORT_TEMPLATE_FILE_NAME = 'shop-import-template.csv';

/** A `POST` body for both the preview and the import: the file's text, and its name for the audit trail. */
export interface ShopImportRequest {
  readonly csv: string;
  readonly fileName: string | null;
}

/** Something wrong with one row. `field` is the CSV column, or `row` for the row as a whole. */
export interface ShopImportProblem {
  readonly field: string;
  readonly message: string;
}

/** One row as the import would create it. */
export interface ShopImportRow {
  /** The spreadsheet row number: the header is row 1, so the first piece is row 2. */
  readonly row: number;
  readonly title: string;
  readonly slug: string;
  readonly sku: string;
  /** The SKU column was blank, so this is the one the import would suggest. */
  readonly skuSuggested: boolean;
  readonly artist: ShopItemArtist | null;
  readonly categories: readonly string[];
  readonly oneOfAKind: boolean;
  readonly startingPriceCents: number | null;
  readonly startingStockQuantity: number | null;
  readonly errors: readonly ShopImportProblem[];
}

export interface ShopImportPreview {
  readonly rowCount: number;
  readonly errorCount: number;
  readonly rows: readonly ShopImportRow[];
}

export interface ShopImportCreated {
  readonly row: number;
  readonly id: string;
  readonly title: string;
  readonly slug: string;
  readonly sku: string;
}

export interface ShopImportResult {
  readonly createdCount: number;
  readonly items: readonly ShopImportCreated[];
}

export function decodeShopImportRow(body: unknown): ShopImportRow | null {
  return decodeShape(body, {
    row: whole,
    title: text,
    slug: text,
    sku: text,
    skuSuggested: flag,
    artist: nullable(credit),
    categories: listOf(text),
    oneOfAKind: flag,
    startingPriceCents: int64,
    startingStockQuantity: int64,
    errors: listOf(shaped({ field: text, message: text })),
  });
}

export function decodeShopImportPreview(body: unknown): ShopImportPreview | null {
  return decodeShape(body, {
    rowCount: whole,
    errorCount: whole,
    rows: listOf((row) => decodeShopImportRow(row) ?? undefined),
  });
}

export function decodeShopImportResult(body: unknown): ShopImportResult | null {
  return decodeShape(body, {
    createdCount: whole,
    items: listOf(shaped({ row: whole, id: text, title: text, slug: text, sku: text })),
  });
}

export type ShopProblem = ApiProblem;

export const decodeShopProblem = decodeApiProblem;

/** One row of the Deleted view (`ShopItemDeletedServiceModel`, ADR 0052). Admin only. */
export type ShopItemDeletedRow = DeletedRow<'title'>;

export type ShopItemDeletedList = DeletedList<'title'>;

export function decodeShopItemDeletedList(body: unknown): ShopItemDeletedList | null {
  return decodeDeletedList(body, 'title');
}
