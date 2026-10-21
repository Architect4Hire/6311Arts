import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ConfirmService } from '../../core/confirm/confirm.service';
import { SessionService } from '../../core/session/session.service';
import { MediaAssetSummary } from '../../models/media';
import { ASSET } from '../../models/media.fixtures';
import { MediaEditor } from './media-editor/media-editor';
import { MediaGrid } from './media-grid/media-grid';
import { MediaLibrary, toSummary } from './media-library/media-library';
import { MediaPicker } from './media-picker/media-picker';
import { MediaUploader } from './media-uploader/media-uploader';

const LIST = '/api/v1/cms/media';
const SUMMARY: MediaAssetSummary = toSummary(ASSET);

async function configure(roles: string[] = ['Editor']) {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  const http = TestBed.inject(HttpTestingController);
  const session = TestBed.inject(SessionService);
  const loaded = session.ensureLoaded();
  http.expectOne('/bff/session').flush({ displayName: 'Ada', roles });
  await loaded;
  return { http, confirm: TestBed.inject(ConfirmService) };
}

/** Lets awaited HTTP promises settle, then renders. */
async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) {
  await Promise.resolve();
  await fixture.whenStable();
  fixture.detectChanges();
}

/** Under fake timers whenStable never settles; draining microtasks lets awaited HTTP promises finish. */
async function drain(fixture: { detectChanges(): void }) {
  for (let i = 0; i < 10; i++) await Promise.resolve();
  fixture.detectChanges();
}

const text = (element: Element) => element.textContent?.replace(/\s+/g, ' ').trim() ?? '';

describe('MediaGrid', () => {
  it('names each tile by its visible text and flags what needs doing', async () => {
    await configure();
    const fixture = TestBed.createComponent(MediaGrid);
    fixture.componentRef.setInput('label', 'Images');
    fixture.componentRef.setInput('items', [
      SUMMARY,
      { ...SUMMARY, id: 'b', processingStatus: 'Pending', thumbnailUrl: null, altText: 'A wall' },
    ]);
    fixture.detectChanges();
    const tiles = [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll('button.tile'),
    ] as HTMLElement[];

    expect(tiles).toHaveLength(2);
    expect(text(tiles[0])).toContain('gallery.jpg');
    expect(text(tiles[0])).toContain('Needs alt text');
    expect(tiles[0].querySelector('img')?.getAttribute('alt')).toBe('');
    expect(text(tiles[1])).toContain('Processing');
    expect(text(tiles[1])).not.toContain('Needs alt text');
  });
});

describe('MediaUploader', () => {
  function file(name: string, type: string, size = 10) {
    const bytes = new Uint8Array(Math.min(size, 16));
    const value = new File([bytes], name, { type });
    if (size > 16) Object.defineProperty(value, 'size', { value: size });
    return value;
  }

  it('refuses a file that is too large or not an image before sending anything', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(MediaUploader);
    fixture.detectChanges();

    fixture.componentInstance.add([
      file('huge.jpg', 'image/jpeg', 26 * 1024 * 1024),
      file('logo.svg', 'image/svg+xml'),
    ]);
    fixture.detectChanges();

    http.expectNone(LIST);
    const alerts = [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll('.item__status--error'),
    ].map(text);
    expect(alerts).toEqual([
      expect.stringContaining('Too large'),
      expect.stringContaining('Not a JPEG'),
    ]);
  });

  it('uploads one file at a time and reports what compression saved', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(MediaUploader);
    const uploaded: string[] = [];
    fixture.componentInstance.uploaded.subscribe((asset) => uploaded.push(asset.id));
    fixture.detectChanges();

    fixture.componentInstance.add([file('a.jpg', 'image/jpeg'), file('b.jpg', 'image/jpeg')]);
    expect(fixture.componentInstance.busy()).toBe(true);

    // Only the first is in flight.
    http.expectOne(LIST).flush(ASSET, { status: 201, statusText: 'Created' });
    http.expectOne(LIST).flush({ ...ASSET, id: 'second' }, { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    expect(uploaded).toEqual([ASSET.id, 'second']);
    expect(text(fixture.nativeElement)).toContain('75% smaller');
    expect(fixture.componentInstance.busy()).toBe(false);
  });

  it('says a duplicate is already there and offers to show it', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(MediaUploader);
    const shown: string[] = [];
    fixture.componentInstance.showExisting.subscribe((id) => shown.push(id));
    fixture.detectChanges();

    fixture.componentInstance.add([file('a.jpg', 'image/jpeg')]);
    http
      .expectOne(LIST)
      .flush(
        { title: 'x', status: 409, code: 'media.duplicate', existingAssetId: 'first' },
        { status: 409, statusText: 'Conflict' },
      );
    fixture.detectChanges();

    expect(text(fixture.nativeElement)).toContain('Already in the library');
    (
      [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
        (b) => text(b) === 'Show it',
      ) as HTMLElement
    ).click();
    expect(shown).toEqual(['first']);
  });

  it('treats a cancelled upload as cancelled, not failed', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(MediaUploader);
    fixture.detectChanges();

    fixture.componentInstance.add([file('a.jpg', 'image/jpeg')]);
    fixture.detectChanges();
    const request = http.expectOne(LIST);
    (
      [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
        (b) => text(b) === 'Cancel',
      ) as HTMLElement
    ).click();
    fixture.detectChanges();

    expect(request.cancelled).toBe(true);
    expect(text(fixture.nativeElement)).toContain('Cancelled');
    expect((fixture.nativeElement as HTMLElement).querySelector('.item__status--error')).toBeNull();
    expect(fixture.componentInstance.busy()).toBe(false);
  });
});

describe('MediaEditor', () => {
  async function render(roles: string[] = ['Editor']) {
    const context = await configure(roles);
    const fixture = TestBed.createComponent(MediaEditor);
    fixture.componentRef.setInput('assetId', ASSET.id);
    fixture.detectChanges();
    context.http.expectOne(`${LIST}/${ASSET.id}`).flush(ASSET);
    await settle(fixture);
    return { ...context, fixture, element: fixture.nativeElement as HTMLElement };
  }

  function typeAltText(element: HTMLElement, value: string) {
    const textarea = element.querySelector('textarea') as HTMLTextAreaElement;
    textarea.value = value;
    textarea.dispatchEvent(new Event('input'));
  }

  it('saves only what changed, under the row version it read', async () => {
    const { http, fixture, element } = await render();
    expect(fixture.componentInstance.dirty()).toBe(false);

    typeAltText(element, 'A long white wall hung with small paintings');
    fixture.detectChanges();
    expect(fixture.componentInstance.dirty()).toBe(true);
    (element.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const request = http.expectOne(`${LIST}/${ASSET.id}`);
    expect(request.request.body).toEqual({
      altText: 'A long white wall hung with small paintings',
    });
    expect(request.request.headers.get('If-Match')).toBe(ASSET.etag);
    request.flush({
      ...ASSET,
      altText: 'A long white wall hung with small paintings',
      etag: '"new"',
    });
    await settle(fixture);

    expect(fixture.componentInstance.dirty()).toBe(false);
  });

  it('shows a conflict beside the editor’s own changes and can keep them', async () => {
    const { http, fixture, element } = await render();
    typeAltText(element, 'Mine');
    fixture.detectChanges();
    (element.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    http
      .expectOne(`${LIST}/${ASSET.id}`)
      .flush(
        { title: 'x', status: 409, code: 'concurrency.conflict' },
        { status: 409, statusText: 'Conflict' },
      );
    await settle(fixture);
    http.expectOne(`${LIST}/${ASSET.id}`).flush({ ...ASSET, altText: 'Theirs', etag: '"theirs"' });
    await settle(fixture);

    const compare = text(element.querySelector('table.compare') as HTMLElement);
    expect(compare).toContain('Mine');
    expect(compare).toContain('Theirs');

    (
      [...element.querySelectorAll('button')].find(
        (b) => text(b) === 'Keep my changes',
      ) as HTMLElement
    ).click();
    await settle(fixture);
    const retry = http.expectOne(`${LIST}/${ASSET.id}`);
    expect(retry.request.headers.get('If-Match')).toBe('"theirs"');
    expect(retry.request.body).toEqual({ altText: 'Mine' });
  });

  it('puts an alt-text refusal on the field and keeps the draft', async () => {
    const { http, fixture, element } = await render();
    typeAltText(element, 'IMG_2041');
    fixture.detectChanges();
    (element.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    http.expectOne(`${LIST}/${ASSET.id}`).flush(
      {
        title: 'x',
        status: 400,
        code: 'validation.failed',
        errors: { altText: ['Describe what’s in the picture; the file name isn’t a description.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle(fixture);

    expect(text(element)).toContain('the file name isn’t a description');
    expect((element.querySelector('textarea') as HTMLTextAreaElement).value).toBe('IMG_2041');
    expect(fixture.componentInstance.dirty()).toBe(true);
  });

  it('offers delete to Admins only, and explains an image in use', async () => {
    const editor = await render(['Editor']);
    expect(text(editor.element)).not.toContain('Delete image');
    TestBed.resetTestingModule();

    const { http, fixture, element, confirm } = await render(['Admin']);
    vi.spyOn(confirm, 'confirm').mockResolvedValue(true);
    (
      [...element.querySelectorAll('button')].find((b) => text(b) === 'Delete image') as HTMLElement
    ).click();
    await settle(fixture);

    const request = http.expectOne(`${LIST}/${ASSET.id}`);
    expect(request.request.method).toBe('DELETE');
    request.flush(
      { title: 'x', status: 409, code: 'media.in_use' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle(fixture);

    expect(text(element)).toContain('used by content or the shop');
  });

  it('refreshes itself while the website sizes are being made', async () => {
    vi.useFakeTimers();
    try {
      const context = await configure();
      const fixture = TestBed.createComponent(MediaEditor);
      fixture.componentRef.setInput('assetId', ASSET.id);
      fixture.detectChanges();
      context.http
        .expectOne(`${LIST}/${ASSET.id}`)
        .flush({ ...ASSET, processingStatus: 'Pending', renditions: [] });
      await drain(fixture);
      expect(text(fixture.nativeElement)).toContain('Renditions are being made');

      await vi.advanceTimersByTimeAsync(3000);
      context.http.expectOne(`${LIST}/${ASSET.id}`).flush(ASSET);
      await drain(fixture);

      expect((fixture.nativeElement as HTMLElement).querySelector('img')?.getAttribute('src')).toBe(
        ASSET.renditions[1].url,
      );
      expect(text(fixture.nativeElement)).toContain('The website sizes are ready');
    } finally {
      vi.useRealTimers();
    }
  });
});

describe('MediaLibrary', () => {
  async function render() {
    const context = await configure();
    const fixture = TestBed.createComponent(MediaLibrary);
    fixture.detectChanges();
    return { ...context, fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('loads, shows the empty state, and asks for the filter that is chosen', async () => {
    const { http, fixture, element } = await render();
    expect(text(element)).toContain('Loading the library');

    http.expectOne((r) => r.url === LIST).flush({ items: [], nextCursor: null });
    await settle(fixture);
    expect(text(element)).toContain('No images yet');

    (
      [...element.querySelectorAll('button')].find(
        (b) => text(b) === 'Needs alt text',
      ) as HTMLElement
    ).click();
    await settle(fixture);
    const filtered = http.expectOne((r) => r.url === LIST);
    expect(filtered.request.params.get('missingAltText')).toBe('true');
    filtered.flush({ items: [SUMMARY], nextCursor: 'next' });
    await settle(fixture);

    expect(element.querySelectorAll('button.tile')).toHaveLength(1);
    expect(text(element)).toContain('Load more');
  });

  it('says so when the library cannot be loaded, and can try again', async () => {
    const { http, fixture, element } = await render();

    http.expectOne((r) => r.url === LIST).flush(null, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);
    expect(text(element)).toContain('couldn’t be loaded');

    (
      [...element.querySelectorAll('button')].find((b) => text(b) === 'Try again') as HTMLElement
    ).click();
    await settle(fixture);
    http.expectOne((r) => r.url === LIST).flush({ items: [SUMMARY], nextCursor: null });
    await settle(fixture);
    expect(element.querySelectorAll('button.tile')).toHaveLength(1);
  });

  it('counts unsaved alt text and an upload in flight as work to warn about', async () => {
    const { http, fixture, element } = await render();
    http.expectOne((r) => r.url === LIST).flush({ items: [SUMMARY], nextCursor: null });
    await settle(fixture);
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);

    (element.querySelector('button.tile') as HTMLElement).click();
    await settle(fixture);
    http.expectOne(`${LIST}/${ASSET.id}`).flush(ASSET);
    await settle(fixture);
    const textarea = element.querySelector('textarea') as HTMLTextAreaElement;
    textarea.value = 'Something new';
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
  });

  it('drops the answer to a filter that has since changed', async () => {
    const { http, fixture, element } = await render();
    const first = http.expectOne((r) => r.url === LIST);

    (
      [...element.querySelectorAll('button')].find((b) => text(b) === 'Failed') as HTMLElement
    ).click();
    await settle(fixture);
    const second = http.expectOne((r) => r.url === LIST);
    second.flush({ items: [], nextCursor: null });
    await settle(fixture);

    // The slow answer for "All images" arrives last and must not replace the "Failed" list.
    first.flush({ items: [SUMMARY], nextCursor: null });
    await settle(fixture);
    expect(element.querySelectorAll('button.tile')).toHaveLength(0);
    expect(text(element)).toContain('No image has failed');
  });

  it('keeps a failed library failed when an upload finishes', async () => {
    const { http, fixture, element } = await render();
    http.expectOne((r) => r.url === LIST).flush(null, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);

    (fixture.componentInstance as unknown as { onUploaded(asset: typeof ASSET): void }).onUploaded(
      ASSET,
    );
    fixture.detectChanges();

    expect(text(element)).toContain('couldn’t be loaded');
    expect(element.querySelectorAll('button.tile')).toHaveLength(0);
  });

  it('says when more images could not be loaded and keeps the ones shown', async () => {
    const { http, fixture, element } = await render();
    http.expectOne((r) => r.url === LIST).flush({ items: [SUMMARY], nextCursor: 'next' });
    await settle(fixture);

    (
      [...element.querySelectorAll('button')].find((b) => text(b) === 'Load more') as HTMLElement
    ).click();
    await settle(fixture);
    http.expectOne((r) => r.url === LIST).flush(null, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);

    expect(element.querySelectorAll('button.tile')).toHaveLength(1);
    expect(text(element)).toContain('More images couldn’t be loaded');
    expect(text(element)).toContain('Try loading more again');
  });
});

describe('MediaPicker', () => {
  it('loads when opened, warns about missing alt text, and emits the choice', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(MediaPicker);
    const picked: string[] = [];
    fixture.componentInstance.picked.subscribe((item) => picked.push(item.id));
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();

    http.expectOne((r) => r.url === LIST).flush({ items: [SUMMARY], nextCursor: null });
    await settle(fixture);
    const element = fixture.nativeElement as HTMLElement;
    const use = [...element.querySelectorAll('button')].find(
      (b) => text(b) === 'Use this image',
    ) as HTMLButtonElement;
    expect(use.disabled).toBe(true);

    (element.querySelector('button.tile') as HTMLElement).click();
    fixture.detectChanges();
    expect(text(element)).toContain('no alt text yet');
    expect(use.disabled).toBe(false);

    use.click();
    expect(picked).toEqual([ASSET.id]);
    expect(fixture.componentInstance.open()).toBe(false);
  });
});
