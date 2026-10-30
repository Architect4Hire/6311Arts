import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { ConfirmService } from '../../core/confirm/confirm.service';
import { SessionService } from '../../core/session/session.service';
import { ARTIST, toArtistSummary } from '../../models/artist.fixtures';
import { ShopItem } from '../../models/shop';
import { SHOP_ITEM, SYNCED_SHOP_ITEM, toShopItemSummary } from '../../models/shop.fixtures';
import { ShopItemEditor } from './shop-item-editor/shop-item-editor';
import { ShopList } from './shop-list/shop-list';

const BASE = '/api/v1/cms/shop';
const ONE = `${BASE}/${SHOP_ITEM.id}`;
const ARTISTS = '/api/v1/cms/artists';
const MEDIA = '/api/v1/cms/media';

async function configure(roles: string[] = ['Editor']) {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });
  const http = TestBed.inject(HttpTestingController);
  const session = TestBed.inject(SessionService);
  const loaded = session.ensureLoaded();
  http.expectOne('/bff/session').flush({ displayName: 'Ada', roles });
  await loaded;
  return { http, confirm: TestBed.inject(ConfirmService), router: TestBed.inject(Router) };
}

/** Lets awaited HTTP promises settle, then renders. */
async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) {
  for (let i = 0; i < 5; i++) await Promise.resolve();
  await fixture.whenStable();
  fixture.detectChanges();
}

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

function button(root: HTMLElement, label: string): HTMLButtonElement | undefined {
  return [...root.querySelectorAll('button')].find(
    (candidate) => text(candidate) === label || candidate.getAttribute('aria-label') === label,
  );
}

function input(root: HTMLElement, name: string): HTMLInputElement | HTMLTextAreaElement {
  return root.querySelector(`[name="${name}"]`) as HTMLInputElement | HTMLTextAreaElement;
}

function type(root: HTMLElement, name: string, value: string) {
  const control = input(root, name);
  control.value = value;
  control.dispatchEvent(new Event('input'));
}

/** The editor asks Media about each photograph it shows; a piece with one answers one request. */
function flushGallery(http: HttpTestingController, item: ShopItem) {
  for (const id of item.imageMediaIds) {
    http.expectOne(`${MEDIA}/${id}`).flush({
      id,
      kind: 'Original',
      fileName: 'piece.jpg',
      mimeType: 'image/jpeg',
      bytes: 1000,
      width: 1600,
      height: 1000,
      checksum: 'a'.repeat(64),
      processingStatus: 'Ready',
      visibility: 'Draft',
      altText: 'The cuff from the front.',
      renditions: [{ name: '400.webp', url: `/media/${id}/400.webp`, width: 400, height: 250 }],
      uploadedBy: 'editor-1',
      uploadedAt: '2026-10-20T12:00:00+00:00',
      updatedAt: '2026-10-20T12:00:00+00:00',
      etag: '"img"',
    });
  }
}

async function openEditor(item: ShopItem | null, roles: string[] = ['Editor']) {
  const context = await configure(roles);
  const fixture = TestBed.createComponent(ShopItemEditor);
  if (item) fixture.componentRef.setInput('id', item.id);
  fixture.detectChanges();
  if (item) {
    context.http.expectOne(`${BASE}/${item.id}`).flush(item);
    await settle(fixture);
    flushGallery(context.http, item);
    await settle(fixture);
  }
  return { ...context, fixture, root: fixture.nativeElement as HTMLElement };
}

describe('ShopList', () => {
  it('lists artwork with its artist, status and what Square says', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(ShopList);
    fixture.detectChanges();

    expect(text(fixture.nativeElement.querySelector('[role="status"]'))).toBe('Loading artwork…');
    http
      .expectOne((r) => r.url === BASE)
      .flush({
        items: [
          toShopItemSummary(SHOP_ITEM),
          {
            ...toShopItemSummary(SYNCED_SHOP_ITEM),
            id: 'b',
            title: 'Serpentine Pendant',
            status: 'Published',
          },
        ],
        nextCursor: null,
      });
    await settle(fixture);

    const rows = [...(fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr')];
    expect(rows.map((row) => text(row.querySelector('th a')))).toEqual([
      'Copper Cuff',
      'Serpentine Pendant',
    ]);
    expect(text(rows[0])).toContain('ART-9 · Jewelry · One of a kind');
    expect(text(rows[0])).toContain('Carrie Haase');
    expect(text(rows[0])).toContain('Not in Square yet');
    expect(text(rows[1])).toContain('$120.00 · 1 in stock');
    expect(rows[0].querySelector('a')?.getAttribute('href')).toBe(`/shop/${SHOP_ITEM.id}`);

    // Many pieces at once come from a CSV, beside the one-at-a-time "New piece".
    const actions = [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll('.head__actions a'),
    ];
    expect(actions.map((link) => [text(link), link.getAttribute('href')])).toEqual([
      ['Import CSV', '/shop/import'],
      ['New piece', '/shop/new'],
    ]);
  });

  it('asks again with the category chosen, and says when nothing matches', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(ShopList);
    fixture.detectChanges();
    http.expectOne((r) => r.url === BASE).flush({ items: [], nextCursor: null });
    await settle(fixture);

    const select = fixture.nativeElement.querySelector('[name="category"]') as HTMLSelectElement;
    select.value = 'ceramics';
    select.dispatchEvent(new Event('change'));
    await settle(fixture);
    const second = http.expectOne((r) => r.url === BASE);

    expect(second.request.params.get('category')).toBe('ceramics');
    second.flush({ items: [], nextCursor: null });
    await settle(fixture);
    expect(text(fixture.nativeElement)).toContain('Nothing is filed under Ceramics.');
  });

  it('offers a retry when the artwork cannot be loaded, and an empty state when there are none', async () => {
    // frontend.md: every data surface has loading, empty, error and success states. All four existed in the
    // component and only the happy path was tested, so a 503 leaving "Loading artwork…" on screen for good
    // would have passed (test-gap review).
    const { http } = await configure();
    const fixture = TestBed.createComponent(ShopList);
    fixture.detectChanges();

    expect(text(fixture.nativeElement)).toContain('Loading artwork…');

    http.expectOne((r) => r.url === BASE).flush(null, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);

    expect(text(fixture.nativeElement)).toContain('The shop couldn’t be loaded');

    button(fixture.nativeElement, 'Try again')!.click();
    http.expectOne((r) => r.url === BASE).flush({ items: [], nextCursor: null });
    await settle(fixture);

    expect(fixture.nativeElement.querySelector('ui-empty-state')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('tbody')).toBeNull();
  });
});

describe('ShopItemEditor', () => {
  it('offers a retry when the record cannot be loaded, and asks again when it is used', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(ShopItemEditor);
    fixture.componentRef.setInput('id', SHOP_ITEM.id);
    fixture.detectChanges();

    http.expectOne(ONE).flush(null, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);

    expect(text(fixture.nativeElement)).toContain('This piece couldn’t be loaded');

    // The retry asks again rather than showing a form built from nothing.
    button(fixture.nativeElement, 'Try again')!.click();
    http
      .expectOne(ONE)
      .flush(
        { code: 'resource.not_found', title: 'Not found.' },
        { status: 404, statusText: 'Not Found' },
      );
    await settle(fixture);

    expect(text(fixture.nativeElement)).not.toContain('This piece couldn’t be loaded');
  });

  it('says there is no such record on a 404, and offers the way back', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(ShopItemEditor);
    fixture.componentRef.setInput('id', SHOP_ITEM.id);
    fixture.detectChanges();

    http
      .expectOne(ONE)
      .flush(
        { code: 'resource.not_found', title: 'Not found.' },
        { status: 404, statusText: 'Not Found' },
      );
    await settle(fixture);

    const root = fixture.nativeElement as HTMLElement;
    expect(text(root)).toContain('There’s no such piece');
    expect(root.querySelector('a[href="/shop"]')).not.toBeNull();
    expect(button(root, 'Try again')).toBeUndefined();
  });

  it('creates a piece with its categories, SKU and one-of-a-kind count', async () => {
    const { http, fixture, root, router } = await openEditor(null);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    type(root, 'title', 'Copper Cuff');
    type(root, 'sku', 'art-9');
    type(root, 'price', '110');
    (input(root, 'category-jewelry') as HTMLInputElement).click();
    await settle(fixture);

    expect(input(root, 'slug').value).toBe('copper-cuff');
    expect(input(root, 'slug').disabled).toBe(true);

    // One of a kind by default, so there is no count to type and none is sent.
    expect(input(root, 'quantity')).toBeNull();
    button(root, 'Create piece')!.click();
    const post = http.expectOne(BASE);

    expect(post.request.body).toEqual({
      title: 'Copper Cuff',
      categories: ['jewelry'],
      sku: 'ART-9',
      oneOfAKind: true,
      startingPriceCents: 11000,
    });

    post.flush(SHOP_ITEM);
    await settle(fixture);
    expect(navigate).toHaveBeenCalledWith(['/shop', SHOP_ITEM.id], {
      replaceUrl: true,
      state: { shopItemCreated: true },
    });
  });

  it('asks for a count only when the piece is not one of a kind', async () => {
    const { http, fixture, root } = await openEditor(null);

    type(root, 'title', 'Mushrooms in Fall');
    (input(root, 'oneOfAKind') as HTMLInputElement).click();
    await settle(fixture);
    type(root, 'quantity', '25');
    await settle(fixture);
    button(root, 'Create piece')!.click();

    expect(http.expectOne(BASE).request.body).toEqual({
      title: 'Mushrooms in Fall',
      oneOfAKind: false,
      startingStockQuantity: 25,
    });
  });

  it('refuses a malformed SKU and a missing count before asking the API', async () => {
    const { http, fixture, root } = await openEditor(null);

    type(root, 'title', 'A Piece');
    type(root, 'sku', 'ART 9');
    (input(root, 'oneOfAKind') as HTMLInputElement).click();
    await settle(fixture);
    type(root, 'quantity', '');
    await settle(fixture);
    button(root, 'Create piece')!.click();
    await settle(fixture);

    http.expectNone(BASE);
    expect(input(root, 'sku').getAttribute('aria-invalid')).toBe('true');
    expect(text(root)).toContain('A SKU is letters, digits, hyphens and dots');
    expect(text(root)).toContain('Enter how many there are');
  });

  it('shows a SKU clash from the API on the SKU field and keeps the edit', async () => {
    const { http, fixture, root } = await openEditor(SHOP_ITEM);

    type(root, 'sku', 'CLS-4-1');
    await settle(fixture);
    button(root, 'Save')!.click();
    http.expectOne(ONE).flush(
      {
        code: 'cms.sku_taken',
        detail: 'The SKU “CLS-4-1” is already in use.',
        errors: {
          sku: ['“CLS-4-1” belongs to another shop item or class session. Choose another.'],
        },
      },
      { status: 409, statusText: 'Conflict' },
    );
    await settle(fixture);

    expect(text(root)).toContain('belongs to another shop item or class session');
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
  });

  it('shows what Square holds read-only once it has reported a price and stock', async () => {
    const { root } = await openEditor(SYNCED_SHOP_ITEM);

    // No editable price or count: both changes are commands to Square (ADR 0032, arriving in 5.4a and 5.4b).
    expect(input(root, 'price')).toBeNull();
    expect(input(root, 'quantity')).toBeNull();
    expect(text(root.querySelector('.price'))).toBe('Price in Square: $120.00');
    expect(text(root)).toContain('1 in stock in Square.');
    expect(button(root, 'Change price')?.disabled).toBe(true);
    expect(button(root, 'Change price')?.getAttribute('aria-describedby')).toBe(
      'square-holds-hint',
    );

    // A one-of-a-kind piece never gets a set-quantity command at all (ADR 0032).
    expect(button(root, 'Set quantity')).toBeUndefined();
    expect(text(root)).toContain('There is only one of this piece');
  });

  it('credits an artist and takes the credit off again', async () => {
    const { http, fixture, root } = await openEditor(SHOP_ITEM);

    expect(text(root.querySelector('cms-artist-picker'))).toContain('Carrie Haase');
    button(root, 'Remove Carrie Haase')!.click();
    await settle(fixture);
    button(root, 'Save')!.click();
    expect(http.expectOne(ONE).request.body).toEqual({ removeArtist: true });
  });

  it('searches the roster and credits someone else', async () => {
    const { http, fixture, root } = await openEditor(SHOP_ITEM);

    // Fake timers only across the search's pause: whenStable waits on real ones.
    vi.useFakeTimers();
    try {
      type(root, 'artistSearch', 'ha');
      vi.advanceTimersByTime(300);
    } finally {
      vi.useRealTimers();
    }
    const search = http.expectOne((r) => r.url === ARTISTS);
    expect(search.request.params.get('search')).toBe('ha');
    search.flush({
      items: [
        toArtistSummary(ARTIST),
        { ...toArtistSummary(ARTIST), id: 'gone', name: 'Gone Artist', status: 'Archived' },
      ],
      nextCursor: null,
    });
    await settle(fixture);

    // Archived artists are not offered.
    expect(button(root, 'Credit Gone Artist')).toBeUndefined();
    button(root, 'Credit Carrie Haase')!.click();
    await settle(fixture);
    button(root, 'Save')!.click();

    expect(http.expectOne(ONE).request.body).toEqual({ artistId: ARTIST.id });
  });

  it('reorders the gallery so another photograph leads, and keeps focus with it', async () => {
    const second = '0199f0a0-0000-7000-8000-0000000000d2';
    const item: ShopItem = { ...SHOP_ITEM, imageMediaIds: [...SHOP_ITEM.imageMediaIds, second] };
    const { http, fixture, root } = await openEditor(item);

    expect(text(root.querySelector('.shot__lead'))).toBe('Lead photograph');
    button(root, 'Move photograph 2 up')!.click();
    await settle(fixture);

    // The picture that moved keeps focus; at the front its "up" is disabled, so "down" has it.
    expect(document.activeElement?.getAttribute('aria-label')).toBe('Move photograph 1 down');

    button(root, 'Save')!.click();
    expect(http.expectOne(ONE).request.body).toEqual({
      imageMediaIds: [second, ...SHOP_ITEM.imageMediaIds],
    });
  });

  it('removes a photograph and moves focus to what took its place', async () => {
    const { http, fixture, root } = await openEditor(SHOP_ITEM);

    button(root, 'Remove photograph 1')!.click();
    await settle(fixture);

    expect(text(document.activeElement)).toBe('Add photograph');
    button(root, 'Save')!.click();
    expect(http.expectOne(ONE).request.body).toEqual({ imageMediaIds: [] });
  });

  it('on a 409 shows both versions and can keep the editor’s changes', async () => {
    const { http, fixture, root } = await openEditor(SHOP_ITEM);
    const theirs: ShopItem = { ...SHOP_ITEM, title: 'Copper Cuff (large)', etag: '"theirs"' };

    type(root, 'title', 'Copper Bangle');
    await settle(fixture);
    button(root, 'Save')!.click();
    http
      .expectOne(ONE)
      .flush({ code: 'concurrency.conflict' }, { status: 409, statusText: 'Conflict' });
    await settle(fixture);
    http.expectOne(ONE).flush(theirs);
    await settle(fixture);

    const comparison = root.querySelector('.compare')!;
    expect(text(comparison)).toContain('Copper Cuff (large)');
    expect(text(comparison)).toContain('Copper Bangle');
    expect(text(comparison)).toContain('Jewelry');
    expect(input(root, 'title').value).toBe('Copper Bangle');

    button(root, 'Keep my changes')!.click();
    const retry = http.expectOne(ONE);
    expect(retry.request.headers.get('If-Match')).toBe('"theirs"');
  });

  it('publishes the saved version, and archive is for Admins', async () => {
    const { http, fixture, root } = await openEditor(SHOP_ITEM, ['Editor']);

    expect(button(root, 'Archive')).toBeUndefined();
    button(root, 'Publish')!.click();
    const publish = http.expectOne(`${ONE}/publish`);
    expect(publish.request.headers.get('If-Match')).toBe(SHOP_ITEM.etag);
    publish.flush({ ...SHOP_ITEM, status: 'Published', slugLocked: true });
    await settle(fixture);

    expect(text(root.querySelector('ui-status-chip'))).toContain('Published');
  });

  it('lets an Admin archive after confirming', async () => {
    const { http, fixture, root, confirm } = await openEditor(SHOP_ITEM, ['Admin']);
    vi.spyOn(confirm, 'confirm').mockResolvedValue(true);

    button(root, 'Archive')!.click();
    await settle(fixture);
    http.expectOne(`${ONE}/archive`).flush({ ...SHOP_ITEM, status: 'Archived' });
    await settle(fixture);

    expect(text(root.querySelector('ui-status-chip'))).toContain('Archived');
  });

  it('refuses to publish while there are unsaved changes', async () => {
    const { http, fixture, root } = await openEditor(SHOP_ITEM);

    type(root, 'title', 'Changed');
    await settle(fixture);

    expect(button(root, 'Publish')?.disabled).toBe(true);
    expect(text(root)).toContain('Save your changes first');
    http.expectNone(`${ONE}/publish`);
  });

  it('offers delete to Admins only, and returns to the list', async () => {
    // ADR 0052: Admin only, like archive, and offered whatever the status.
    const editor = await openEditor(SHOP_ITEM);
    expect(button(editor.root, 'Delete piece')).toBeUndefined();
    TestBed.resetTestingModule();

    const { http, fixture, root, confirm, router } = await openEditor(SHOP_ITEM, ['Admin']);
    const ask = vi.spyOn(confirm, 'confirm').mockResolvedValue(true);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    button(root, 'Delete piece')!.click();
    await settle(fixture);

    expect(ask).toHaveBeenCalledWith(expect.objectContaining({ heading: 'Delete this piece?' }));
    const deleted = http.expectOne(ONE);
    expect(deleted.request.method).toBe('DELETE');
    expect(deleted.request.headers.get('If-Match')).toBe(SHOP_ITEM.etag);
    deleted.flush(null, { status: 204, statusText: 'No Content' });
    await settle(fixture);

    expect(navigate).toHaveBeenCalled();
  });
});
