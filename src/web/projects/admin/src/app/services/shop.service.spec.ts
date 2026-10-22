import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { SUPPRESS_PROBLEM_TOAST } from '../core/http/http-context';
import {
  SHOP_CATEGORIES,
  categoryLabel,
  decodeShopItem,
  decodeShopItemSummary,
  decodeShopImportPreview,
  decodeShopImportResult,
  decodeShopPage,
  isSku,
  normalizeSku,
  shopImportFileName,
  shopImportTemplate,
  toCsv,
} from '../models/shop';
import {
  SHOP_IMPORT_PREVIEW,
  SHOP_IMPORT_RESULT,
  SHOP_ITEM,
  SYNCED_SHOP_ITEM,
  toShopItemSummary,
} from '../models/shop.fixtures';
import { ShopService } from './shop.service';

const BASE = '/api/v1/cms/shop';

function setup() {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  return { shop: TestBed.inject(ShopService), http: TestBed.inject(HttpTestingController) };
}

describe('ShopService', () => {
  it('lists with the filters as query parameters, and shows failures in place', async () => {
    const { shop, http } = setup();

    const list = firstValueFrom(
      shop.list({
        cursor: 'abc',
        limit: 50,
        status: 'Published',
        search: 'cuff',
        category: 'jewelry',
      }),
    );
    const request = http.expectOne((r) => r.url === BASE);
    expect(request.request.params.get('cursor')).toBe('abc');
    expect(request.request.params.get('status')).toBe('Published');
    expect(request.request.params.get('search')).toBe('cuff');
    expect(request.request.params.get('category')).toBe('jewelry');
    expect(request.request.context.get(SUPPRESS_PROBLEM_TOAST)).toBe(true);
    request.flush({ items: [toShopItemSummary(SHOP_ITEM)], nextCursor: null });

    expect(await list).toEqual({ items: [toShopItemSummary(SHOP_ITEM)], nextCursor: null });
  });

  it('sends an edit and a status command under the ETag it was given', async () => {
    const { shop, http } = setup();

    const edit = firstValueFrom(shop.update(SHOP_ITEM.id, { description: '' }, SHOP_ITEM.etag));
    const patch = http.expectOne(`${BASE}/${SHOP_ITEM.id}`);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.headers.get('If-Match')).toBe(SHOP_ITEM.etag);
    expect(patch.request.body).toEqual({ description: '' });
    patch.flush(SHOP_ITEM);
    await edit;

    const published = firstValueFrom(shop.command(SHOP_ITEM.id, 'publish', SHOP_ITEM.etag));
    const command = http.expectOne(`${BASE}/${SHOP_ITEM.id}/publish`);
    expect(command.request.headers.get('If-Match')).toBe(SHOP_ITEM.etag);
    expect(command.request.body).toBeNull();
    command.flush({ ...SHOP_ITEM, status: 'Published' });
    expect((await published).status).toBe('Published');
  });

  it('sends the schedule command with its time in the body', async () => {
    const { shop, http } = setup();

    const scheduled = firstValueFrom(
      shop.schedule(SHOP_ITEM.id, '2026-12-24T17:00:00+00:00', SHOP_ITEM.etag),
    );
    const request = http.expectOne(`${BASE}/${SHOP_ITEM.id}/schedule`);
    expect(request.request.body).toEqual({ scheduledFor: '2026-12-24T17:00:00+00:00' });
    request.flush({
      ...SHOP_ITEM,
      status: 'Scheduled',
      scheduledFor: '2026-12-24T17:00:00+00:00',
    });

    expect((await scheduled).scheduledFor).toBe('2026-12-24T17:00:00+00:00');
  });

  it('refuses a body that is not a shop item', async () => {
    const { shop, http } = setup();

    const read = firstValueFrom(shop.get(SHOP_ITEM.id));
    http.expectOne(`${BASE}/${SHOP_ITEM.id}`).flush({ id: SHOP_ITEM.id });

    await expect(read).rejects.toThrow('not a shop item');
  });
});

describe('shop models', () => {
  it('decodes exactly the API shape', () => {
    expect(decodeShopItem(SHOP_ITEM)).toEqual(SHOP_ITEM);
    expect(decodeShopItem(SYNCED_SHOP_ITEM)).toEqual(SYNCED_SHOP_ITEM);
    expect(decodeShopItem({ ...SHOP_ITEM, artist: null })?.artist).toBeNull();
    expect(decodeShopItem({ ...SHOP_ITEM, description: null })?.description).toBeNull();
    expect(decodeShopItem({ ...SHOP_ITEM, artist: { id: 'a' } })).toBeNull();
    expect(decodeShopItem({ ...SHOP_ITEM, categories: [1] })).toBeNull();
    expect(decodeShopItem({ ...SHOP_ITEM, imageMediaIds: undefined })).toBeNull();
    expect(decodeShopItem({ ...SHOP_ITEM, oneOfAKind: 'yes' })).toBeNull();
    expect(decodeShopItem({ ...SHOP_ITEM, startingStockEditable: undefined })).toBeNull();
  });

  it('takes an integer the schema allows as a string, and refuses a half one', () => {
    expect(decodeShopItem({ ...SHOP_ITEM, itemNumber: '9' })?.itemNumber).toBe(9);
    expect(decodeShopItem({ ...SHOP_ITEM, itemNumber: '9.5' })).toBeNull();
    expect(decodeShopItem({ ...SHOP_ITEM, priceCents: '12000' })?.priceCents).toBe(12000);
    expect(
      decodeShopItemSummary({ ...toShopItemSummary(SHOP_ITEM), stockOnHand: '3' })?.stockOnHand,
    ).toBe(3);
  });

  it('decodes a page and refuses one whose rows are not summaries', () => {
    expect(
      decodeShopPage({ items: [toShopItemSummary(SHOP_ITEM)], nextCursor: 'x' })?.nextCursor,
    ).toBe('x');
    expect(decodeShopPage({ items: [{}], nextCursor: null })).toBeNull();
    expect(decodeShopPage({ items: 'none', nextCursor: null })).toBeNull();
  });

  it('matches the API on what a SKU looks like and how it is written', () => {
    expect(normalizeSku('  art-9 ')).toBe('ART-9');
    expect(isSku('ART-9')).toBe(true);
    expect(isSku('CUFF.001')).toBe(true);
    expect(isSku('ART 9')).toBe(false);
    expect(isSku('ART--9')).toBe(false);
    expect(isSku('A'.repeat(65))).toBe(false);
  });

  it('labels the categories it knows and leaves anything else as it came', () => {
    expect(SHOP_CATEGORIES.map((category) => category.slug)).toEqual([
      'jewelry',
      'prints',
      'ceramics',
      'painting',
      'sculpture',
      'books-magazines',
    ]);
    expect(categoryLabel('books-magazines')).toBe('Books and magazines');
    expect(categoryLabel('something-else')).toBe('something-else');
  });

  it('asks for a preview of the file, quietly, without a key', async () => {
    const { shop, http } = setup();

    const preview = firstValueFrom(
      shop.previewImport({ csv: 'title\r\nCuff\r\n', fileName: 'art.csv' }),
    );
    const request = http.expectOne(`${BASE}/import/preview`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ csv: 'title\r\nCuff\r\n', fileName: 'art.csv' });
    expect(request.request.headers.has('Idempotency-Key')).toBe(false);
    expect(request.request.context.get(SUPPRESS_PROBLEM_TOAST)).toBe(true);
    request.flush(SHOP_IMPORT_PREVIEW);

    expect(await preview).toEqual(SHOP_IMPORT_PREVIEW);
  });

  it('imports under the idempotency key it was given', async () => {
    const { shop, http } = setup();

    const imported = firstValueFrom(
      shop.importItems({ csv: 'title\r\nCuff\r\n', fileName: null }, 'key-1'),
    );
    const request = http.expectOne(`${BASE}/import`);
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Idempotency-Key')).toBe('key-1');
    expect(request.request.body).toEqual({ csv: 'title\r\nCuff\r\n', fileName: null });
    expect(request.request.context.get(SUPPRESS_PROBLEM_TOAST)).toBe(true);
    request.flush(SHOP_IMPORT_RESULT);

    expect(await imported).toEqual(SHOP_IMPORT_RESULT);
  });

  it('refuses an import answer that is not one', async () => {
    const { shop, http } = setup();

    const preview = firstValueFrom(shop.previewImport({ csv: 'x', fileName: null }));
    http.expectOne(`${BASE}/import/preview`).flush({ rowCount: 1, errorCount: 0, rows: [{}] });
    await expect(preview).rejects.toThrow('import preview');
  });

  it('decodes a preview and a result strictly', () => {
    const row = SHOP_IMPORT_PREVIEW.rows[1];
    expect(decodeShopImportPreview(SHOP_IMPORT_PREVIEW)).toEqual(SHOP_IMPORT_PREVIEW);
    expect(
      decodeShopImportPreview({ ...SHOP_IMPORT_PREVIEW, rows: [{ ...row, skuSuggested: 'no' }] }),
    ).toBeNull();
    expect(
      decodeShopImportPreview({
        ...SHOP_IMPORT_PREVIEW,
        rows: [{ ...row, errors: [{ field: 'sku' }] }],
      }),
    ).toBeNull();
    expect(decodeShopImportPreview({ ...SHOP_IMPORT_PREVIEW, errorCount: undefined })).toBeNull();
    expect(decodeShopImportResult(SHOP_IMPORT_RESULT)).toEqual(SHOP_IMPORT_RESULT);
    expect(decodeShopImportResult({ createdCount: 1, items: [{ row: 2, id: 'n1' }] })).toBeNull();
  });

  it('writes RFC 4180 rows, quoting only what needs it', () => {
    expect(toCsv([['a', 'b,c', 'say "hi"', 'one\ntwo']])).toBe(
      'a,"b,c","say ""hi""","one\ntwo"\r\n',
    );
  });

  it('tidies a file name into one the API accepts, rather than refusing the import', () => {
    expect(shopImportFileName('artwork.csv')).toBe('artwork.csv');
    expect(shopImportFileName('art\u0007work\n.csv')).toBe('artwork.csv');
    expect(shopImportFileName('  ' + '\u0000')).toBeNull();
    expect(shopImportFileName('a'.repeat(300))).toBe('a'.repeat(255));
    // Cut at 255 UTF-16 units, but never through the middle of an emoji.
    expect(shopImportFileName('a'.repeat(254) + '\uD83C\uDFA8.csv')).toBe('a'.repeat(254));
  });

  it('builds the template behind a byte-order mark, with the multi-line example quoted', () => {
    const template = shopImportTemplate();

    expect(template.startsWith('\uFEFF')).toBe(true);
    expect(template.slice(1).split('\r\n')[0]).toBe(
      'title,sku,artist,categories,one_of_a_kind,starting_price,starting_stock,slug,description',
    );
    expect(template).toContain(
      'Blue Heron,,,prints,false,45.00,10,,"Giclée print on archival paper.\n\nSigned and numbered."\r\n',
    );
    expect(template).toContain(
      'Raku Bowl,CER-001,,ceramics,true,180,1,raku-bowl,Hand-thrown raku-fired bowl.\r\n',
    );
  });
});
