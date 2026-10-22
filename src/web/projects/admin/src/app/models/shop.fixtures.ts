import { ShopImportPreview, ShopImportResult, ShopItem, ShopItemSummary } from './shop';

/** One draft piece as the API returns it, for specs: a one-of-a-kind cuff with a credited artist. */
export const SHOP_ITEM: ShopItem = {
  id: '0199f0a0-0000-7000-8000-0000000000d1',
  title: 'Copper Cuff',
  slug: 'copper-cuff',
  description: '<p>A forged copper cuff with a hammered face.</p>',
  artist: {
    id: '0199f0a0-0000-7000-8000-0000000000a1',
    name: 'Carrie Haase',
    slug: 'carrie-haase',
    status: 'Published',
  },
  categories: ['jewelry'],
  imageMediaIds: ['0199f0a0-0000-7000-8000-0000000000i1'],
  descriptionImageMediaIds: [],
  sku: 'ART-9',
  itemNumber: 9,
  oneOfAKind: true,
  startingPriceCents: 11000,
  startingStockQuantity: 1,
  currency: 'USD',
  startingPriceEditable: true,
  startingStockEditable: true,
  priceCents: null,
  stockOnHand: null,
  soldOut: false,
  squareHasRecord: false,
  status: 'Draft',
  publishedAt: null,
  scheduledFor: null,
  slugLocked: false,
  legacyWordPressId: null,
  createdAt: '2026-10-20T12:00:00+00:00',
  updatedAt: '2026-10-20T12:00:00+00:00',
  updatedBy: 'editor-1',
  etag: '"0123456789abcdef0123456789abcdef"',
};

/** The same piece after its first sync: Square holds the price and one in stock. */
export const SYNCED_SHOP_ITEM: ShopItem = {
  ...SHOP_ITEM,
  startingPriceEditable: false,
  startingStockEditable: false,
  priceCents: 12000,
  stockOnHand: 1,
  squareHasRecord: true,
};

export function toShopItemSummary(item: ShopItem): ShopItemSummary {
  return {
    id: item.id,
    title: item.title,
    slug: item.slug,
    status: item.status,
    artist: item.artist,
    categories: item.categories,
    leadImageMediaId: item.imageMediaIds[0] ?? null,
    sku: item.sku,
    oneOfAKind: item.oneOfAKind,
    priceCents: item.priceCents,
    stockOnHand: item.stockOnHand,
    soldOut: item.soldOut,
    currency: item.currency,
    updatedAt: item.updatedAt,
  };
}

/** A preview of a two-row file as the API returns it: one row ready, one with two problems. */
export const SHOP_IMPORT_PREVIEW: ShopImportPreview = {
  rowCount: 2,
  errorCount: 1,
  rows: [
    {
      row: 2,
      title: 'Blue Heron',
      slug: 'blue-heron',
      sku: 'ART-12',
      skuSuggested: true,
      artist: SHOP_ITEM.artist,
      categories: ['prints'],
      oneOfAKind: false,
      startingPriceCents: 4500,
      startingStockQuantity: 10,
      errors: [],
    },
    {
      row: 3,
      title: 'Raku Bowl',
      slug: 'raku-bowl',
      sku: 'CER-001',
      skuSuggested: false,
      artist: null,
      categories: ['ceramics'],
      oneOfAKind: true,
      startingPriceCents: null,
      startingStockQuantity: 1,
      errors: [
        { field: 'sku', message: '“CER-001” belongs to another shop item or class session.' },
        { field: 'starting_price', message: 'Enter the price in dollars, such as 45 or 45.00.' },
      ],
    },
  ],
};

/** The same file with its problems fixed. */
export const READY_IMPORT_PREVIEW: ShopImportPreview = {
  ...SHOP_IMPORT_PREVIEW,
  errorCount: 0,
  rows: SHOP_IMPORT_PREVIEW.rows.map((row) => ({ ...row, errors: [] })),
};

export const SHOP_IMPORT_RESULT: ShopImportResult = {
  createdCount: 2,
  items: [
    { row: 2, id: 'n1', title: 'Blue Heron', slug: 'blue-heron', sku: 'ART-12' },
    { row: 3, id: 'n2', title: 'Raku Bowl', slug: 'raku-bowl', sku: 'CER-001' },
  ],
};
