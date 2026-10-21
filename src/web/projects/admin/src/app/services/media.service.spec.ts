import { HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom, toArray } from 'rxjs';
import { SUPPRESS_PROBLEM_TOAST } from '../core/http/http-context';
import {
  decodeAsset,
  decodeMediaProblem,
  decodePage,
  formatBytes,
  savingPercent,
} from '../models/media';
import { ASSET } from '../models/media.fixtures';
import { MediaService, UploadProgress } from './media.service';



function setup() {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  return { media: TestBed.inject(MediaService), http: TestBed.inject(HttpTestingController) };
}

describe('MediaService', () => {
  it('lists with the filters as camelCase query parameters, and shows failures in place', async () => {
    const { media, http } = setup();

    const page = firstValueFrom(
      media.list({ cursor: 'abc', missingAltText: true, status: 'Pending' }),
    );
    const request = http.expectOne((r) => r.url === '/api/v1/cms/media');
    expect(request.request.params.get('cursor')).toBe('abc');
    expect(request.request.params.get('missingAltText')).toBe('true');
    expect(request.request.params.get('status')).toBe('Pending');
    expect(request.request.context.get(SUPPRESS_PROBLEM_TOAST)).toBe(true);
    request.flush({ items: [], nextCursor: null });

    expect(await page).toEqual({ items: [], nextCursor: null });
  });

  it('uploads the text fields before the file and reports progress, then the asset', async () => {
    const { media, http } = setup();
    const file = new File([new Uint8Array(10)], 'gallery.jpg', { type: 'image/jpeg' });

    const events = firstValueFrom(
      media.upload(file, { altText: 'A wall', compressionPreset: 'Smallest' }).pipe(toArray()),
    );
    const request = http.expectOne('/api/v1/cms/media');
    const body = request.request.body as FormData;
    expect([...body.keys()]).toEqual(['altText', 'compressionPreset', 'file']);
    expect(request.request.reportProgress).toBe(true);

    request.event({ type: HttpEventType.UploadProgress, loaded: 5, total: 10 });
    request.flush(ASSET, { status: 201, statusText: 'Created' });

    expect(await events).toEqual<UploadProgress[]>([
      { kind: 'progress', loaded: 5, total: 10 },
      { kind: 'done', asset: ASSET },
    ]);
  });

  it('sends the row version as If-Match on an edit and a delete', () => {
    const { media, http } = setup();

    media.update(ASSET.id, { altText: 'A wall' }, ASSET.etag).subscribe();
    const patch = http.expectOne(`/api/v1/cms/media/${ASSET.id}`);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.headers.get('If-Match')).toBe(ASSET.etag);
    expect(patch.request.body).toEqual({ altText: 'A wall' });

    media.delete(ASSET.id, ASSET.etag).subscribe();
    const remove = http.expectOne(`/api/v1/cms/media/${ASSET.id}`);
    expect(remove.request.method).toBe('DELETE');
    expect(remove.request.headers.get('If-Match')).toBe(ASSET.etag);
  });

  it('refuses a body that is not what the API promised', async () => {
    const { media, http } = setup();

    const asset = firstValueFrom(media.get(ASSET.id));
    http
      .expectOne(`/api/v1/cms/media/${ASSET.id}`)
      .flush({ ...ASSET, processingStatus: 'Sideways' });

    await expect(asset).rejects.toThrow(/not a media asset/);
  });
});

describe('media models', () => {
  it('decodes what the API returns and nothing else', () => {
    expect(decodeAsset(ASSET)).toEqual(ASSET);
    expect(decodeAsset({ ...ASSET, renditions: [{ name: 1 }] })).toBeNull();
    expect(decodePage({ items: [{}], nextCursor: null })).toBeNull();
    expect(decodePage([])).toBeNull();
  });

  it('reads a refusal’s code, detail, duplicate id and field errors', () => {
    const problem = decodeMediaProblem(
      {
        title: 'x',
        code: 'media.duplicate',
        detail: 'Already there.',
        existingAssetId: 'abc',
        errors: { altText: ['Describe it.'], bad: [1] },
      },
      409,
    );

    expect(problem).toEqual({
      status: 409,
      code: 'media.duplicate',
      detail: 'Already there.',
      existingAssetId: 'abc',
      fieldErrors: { altText: ['Describe it.'] },
      correlationId: null,
    });
    expect(decodeMediaProblem('<html>', 502).code).toBeNull();
  });

  it('formats sizes and savings as people read them', () => {
    expect(formatBytes(900)).toBe('900 B');
    expect(formatBytes(2048)).toBe('2 KB');
    expect(formatBytes(2_400_000)).toBe('2.3 MB');
    expect(savingPercent(2_400_000, 600_000)).toBe(75);
    expect(savingPercent(100, 100)).toBeNull();
    expect(savingPercent(null, 100)).toBeNull();
  });
});
