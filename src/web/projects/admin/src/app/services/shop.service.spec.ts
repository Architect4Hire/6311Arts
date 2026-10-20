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
  decodeShopPage,
  isSku,
  normalizeSku,
} from '../models/shop';
import { SHOP_ITEM, SYNCED_SHOP_ITEM, toShopItemSummary } from '../models/shop.fixtures';
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
      shop.list({ cursor: 'abc', limit: 50, status: 'Published', search: 'cuff', category: 'jewelry' }),
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
    expect(decodeShopItemSummary({ ...toShopItemSummary(SHOP_ITEM), stockOnHand: '3' })?.stockOnHand).toBe(3);
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
});
