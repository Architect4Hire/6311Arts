import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { BrowserDownload } from '../../../core/browser/browser-download';
import { SHOP_IMPORT_LIMITS } from '../../../models/shop';
import {
  READY_IMPORT_PREVIEW,
  SHOP_IMPORT_PREVIEW,
  SHOP_IMPORT_RESULT,
} from '../../../models/shop.fixtures';
import { ShopImport } from './shop-import';

const PREVIEW = '/api/v1/cms/shop/import/preview';
const IMPORT = '/api/v1/cms/shop/import';

const CSV = 'title,sku\r\nBlue Heron,\r\nRaku Bowl,CER-001\r\n';

function setup() {
  const save = vi.fn<(blob: Blob, fileName: string) => void>();
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      { provide: BrowserDownload, useValue: { save } },
    ],
  });
  const fixture = TestBed.createComponent(ShopImport);
  fixture.detectChanges();
  return {
    fixture,
    save,
    http: TestBed.inject(HttpTestingController),
    root: fixture.nativeElement as HTMLElement,
  };
}

/** Lets awaited promises (the file read, the HTTP answer) settle, then renders. */
async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) {
  for (let i = 0; i < 5; i++) await Promise.resolve();
  await fixture.whenStable();
  fixture.detectChanges();
}

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

function button(root: HTMLElement, label: string): HTMLButtonElement | undefined {
  return [...root.querySelectorAll('button')].find((candidate) => text(candidate) === label);
}

/** Chooses a file as the browser would: the input's files, then its change event. */
async function choose(
  fixture: ReturnType<typeof setup>['fixture'],
  content: string,
  name = 'artwork.csv',
) {
  const input = (fixture.nativeElement as HTMLElement).querySelector(
    'input[type="file"]',
  ) as HTMLInputElement;
  const file = new File([content], name, { type: 'text/csv' });
  Object.defineProperty(input, 'files', { value: [file], configurable: true });
  input.dispatchEvent(new Event('change'));
  await settle(fixture);
}

describe('ShopImport', () => {
  it('downloads the template as UTF-8 CSV behind a byte-order mark', async () => {
    const { root, save } = setup();

    button(root, 'Download template')!.click();

    expect(save).toHaveBeenCalledOnce();
    const [blob, fileName] = save.mock.calls[0];
    expect(fileName).toBe('shop-import-template.csv');
    expect(blob.type).toBe('text/csv;charset=utf-8');
    const bytes = new Uint8Array(await blob.arrayBuffer());
    expect([...bytes.slice(0, 3)]).toEqual([0xef, 0xbb, 0xbf]);
    const content = new TextDecoder().decode(bytes.slice(3));
    expect(content.split('\r\n')[0]).toBe(
      'title,sku,artist,categories,one_of_a_kind,starting_price,starting_stock,slug,description',
    );
    expect(content).toContain('"Giclée print on archival paper.\n\nSigned and numbered."');
  });

  it('lists the columns, with title the only one required', () => {
    const { root } = setup();

    const columns = [...root.querySelectorAll('.columns tbody th')].map(text);
    expect(columns[0]).toBe('title, required');
    expect(columns.slice(1).every((column) => !column.includes('required'))).toBe(true);
    expect(columns).toContain('starting_price');
  });

  it('previews a chosen file, marks the rows that need fixing, and keeps Import off', async () => {
    const { fixture, http, root } = setup();

    await choose(fixture, CSV);
    const request = http.expectOne(PREVIEW);
    expect(request.request.body).toEqual({ csv: CSV, fileName: 'artwork.csv' });
    request.flush(SHOP_IMPORT_PREVIEW);
    await settle(fixture);

    expect(text(root.querySelector('.summary'))).toBe(
      'artwork.csv: 1 of 2 rows need fixing. Correct the file and choose it again.',
    );
    const rows = [...root.querySelectorAll('.rows tbody tr')];
    expect(rows).toHaveLength(2);
    expect(text(rows[0])).toContain('Ready');
    expect(text(rows[0])).toContain('ART-12 suggested');
    expect(text(rows[0])).toContain('Carrie Haase');
    expect(text(rows[0])).toContain('$45.00');
    expect(rows[1].classList.contains('row--problem')).toBe(true);
    expect(text(rows[1])).toContain('Needs fixing');
    expect(text(rows[1])).toContain(
      'sku: “CER-001” belongs to another shop item or class session.',
    );
    expect(text(rows[1])).toContain('starting_price: Enter the price in dollars');
    expect(text(rows[1])).toContain('1 (one of a kind)');
    expect(button(root, 'Import 2 pieces')!.disabled).toBe(true);
    // The summary takes focus and is read from there; the live region doesn't say it a second time.
    expect(document.activeElement).toBe(root.querySelector('.summary'));
    expect(text(root.querySelector('[aria-live="polite"]'))).toBe('');
  });

  it('says it is checking through the live region while the preview is out', async () => {
    const { fixture, http, root } = setup();

    await choose(fixture, CSV);

    expect(text(root.querySelector('[aria-live="polite"]'))).toBe('Checking artwork.csv…');
    expect(root.querySelector('.checking [role="status"], p[role="status"]')).toBeNull();
    http.expectOne(PREVIEW).flush(READY_IMPORT_PREVIEW);
    await settle(fixture);
  });

  it('checks the same file again when it is chosen again', async () => {
    const { fixture, http, root } = setup();

    await choose(fixture, CSV);
    expect((root.querySelector('input[type="file"]') as HTMLInputElement).value).toBe('');
    http.expectOne(PREVIEW).flush(SHOP_IMPORT_PREVIEW);
    await settle(fixture);

    await choose(fixture, CSV);
    http.expectOne(PREVIEW).flush(READY_IMPORT_PREVIEW);
    await settle(fixture);

    expect(button(root, 'Import 2 pieces')!.disabled).toBe(false);
  });

  it('sends a tidied file name, never one the API would refuse', async () => {
    const { fixture, http } = setup();

    await choose(fixture, CSV, 'art\u0007work.csv');

    expect(http.expectOne(PREVIEW).request.body.fileName).toBe('artwork.csv');
  });

  it('links back to the list as the editor does', () => {
    const { root } = setup();

    const back = root.querySelector('.back a');
    expect(text(back)).toBe('← All artwork');
    expect(back?.getAttribute('href')).toBe('/shop');
  });

  it('imports a clean file under one key, then says how many drafts it made', async () => {
    const { fixture, http, root } = setup();

    await choose(fixture, CSV);
    http.expectOne(PREVIEW).flush(READY_IMPORT_PREVIEW);
    await settle(fixture);
    expect(text(root.querySelector('.summary'))).toBe('artwork.csv: 2 rows are ready to import.');

    const go = button(root, 'Import 2 pieces')!;
    expect(go.disabled).toBe(false);
    go.click();
    const request = http.expectOne(IMPORT);
    const key = request.request.headers.get('Idempotency-Key');
    expect(key).toMatch(/^[0-9a-f-]{36}$/);
    expect(request.request.body).toEqual({ csv: CSV, fileName: 'artwork.csv' });
    request.flush(SHOP_IMPORT_RESULT);
    await settle(fixture);

    expect(text(root.querySelector('.result ui-notice'))).toContain('Imported 2 pieces as drafts.');
    expect(root.querySelector('.result a')?.getAttribute('href')).toBe('/shop');
    expect(root.querySelector('.rows')).toBeNull();
    expect(document.activeElement).toBe(root.querySelector('.result'));

    // From the result the file field is not on the page; it takes focus once it is back.
    button(root, 'Import another file')!.click();
    await settle(fixture);
    expect(document.activeElement).toBe(root.querySelector('input[type="file"]'));
  });

  it('makes a key without crypto.randomUUID, which only a secure context has', async () => {
    const randomUUID = Object.getOwnPropertyDescriptor(crypto, 'randomUUID');
    Object.defineProperty(crypto, 'randomUUID', { value: undefined, configurable: true });
    try {
      const { fixture, http, root } = setup();

      await choose(fixture, CSV);
      http.expectOne(PREVIEW).flush(READY_IMPORT_PREVIEW);
      await settle(fixture);
      button(root, 'Import 2 pieces')!.click();

      const request = http.expectOne(IMPORT);
      expect(request.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f]{32}$/);
      request.flush(SHOP_IMPORT_RESULT);
      await settle(fixture);
    } finally {
      if (randomUUID) Object.defineProperty(crypto, 'randomUUID', randomUUID);
      else delete (crypto as { randomUUID?: unknown }).randomUUID;
    }
  });

  it('retries a dropped import under the same key', async () => {
    const { fixture, http, root } = setup();

    await choose(fixture, CSV);
    http.expectOne(PREVIEW).flush(READY_IMPORT_PREVIEW);
    await settle(fixture);

    button(root, 'Import 2 pieces')!.click();
    const first = http.expectOne(IMPORT);
    const key = first.request.headers.get('Idempotency-Key');
    first.error(new ProgressEvent('error'), { status: 0 });
    await settle(fixture);
    expect(text(root.querySelector('.import-error'))).toContain('Can’t reach 6311arts.');

    button(root, 'Import 2 pieces')!.click();
    const second = http.expectOne(IMPORT);
    expect(second.request.headers.get('Idempotency-Key')).toBe(key);
    second.flush(SHOP_IMPORT_RESULT, { headers: { 'Idempotent-Replayed': 'true' } });
    await settle(fixture);

    expect(text(root.querySelector('.result'))).toContain('Imported 2 pieces as drafts.');
  });

  it('reads the file again when a SKU was taken meanwhile, and shows why nothing was imported', async () => {
    const { fixture, http, root } = setup();

    await choose(fixture, CSV);
    http.expectOne(PREVIEW).flush(READY_IMPORT_PREVIEW);
    await settle(fixture);

    button(root, 'Import 2 pieces')!.click();
    http
      .expectOne(IMPORT)
      .flush(
        { code: 'cms.sku_taken', detail: 'The SKU “CER-001” is already in use.' },
        { status: 409, statusText: 'Conflict' },
      );
    await settle(fixture);
    http.expectOne(PREVIEW).flush(SHOP_IMPORT_PREVIEW);
    await settle(fixture);

    expect(text(root.querySelector('.import-error'))).toContain(
      'Nothing was imported: The SKU “CER-001” is already in use.',
    );
    expect(button(root, 'Import 2 pieces')!.disabled).toBe(true);
  });

  it('shows what the API found wrong with the file as a whole', async () => {
    const { fixture, http, root } = setup();

    await choose(fixture, 'colour\r\nblue\r\n');
    http.expectOne(PREVIEW).flush(
      {
        code: 'validation.failed',
        detail: 'The file can’t be imported.',
        errors: {
          csv: ['There is no “title” column.', '“colour” is not a column the import knows.'],
        },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle(fixture);

    const problem = root.querySelector('.file-problem');
    expect(text(problem)).toContain('This file can’t be imported');
    expect([...problem!.querySelectorAll('li')].map(text)).toEqual([
      'There is no “title” column.',
      '“colour” is not a column the import knows.',
    ]);
    expect(root.querySelector('.rows')).toBeNull();

    // The field itself is marked, and says why when it is focused.
    const field = root.querySelector('input[type="file"]') as HTMLInputElement;
    expect(field.getAttribute('aria-invalid')).toBe('true');
    const described = field.getAttribute('aria-describedby')!.split(' ');
    expect(described.map((id) => text(root.querySelector(`#${id}`)))).toContain(
      'This file can’t be imported',
    );
  });

  it('refuses a file that is not UTF-8 without sending it', async () => {
    const { fixture, http, root } = setup();

    await choose(fixture, 'title\r\nCaf\uFFFD\r\n');

    http.expectNone(PREVIEW);
    expect(text(root.querySelector('.file-problem'))).toContain('This file isn’t saved as UTF-8');
    expect(text(root.querySelector('.file-problem'))).toContain('CSV UTF-8');
  });

  it('refuses a file over the size limit without sending it', async () => {
    const { fixture, http, root } = setup();

    await choose(fixture, 'title\r\n' + 'a'.repeat(SHOP_IMPORT_LIMITS.maxCharacters));

    http.expectNone(PREVIEW);
    expect(text(root.querySelector('.file-problem'))).toContain('This file is too large');
  });

  it('starts again for another file', async () => {
    const { fixture, http, root } = setup();

    await choose(fixture, CSV);
    http.expectOne(PREVIEW).flush(SHOP_IMPORT_PREVIEW);
    await settle(fixture);

    button(root, 'Choose another file')!.click();
    await settle(fixture);

    expect(root.querySelector('.rows')).toBeNull();
    expect(root.querySelector('.summary')).toBeNull();
  });
});
