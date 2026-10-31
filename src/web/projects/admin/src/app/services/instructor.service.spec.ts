import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { SUPPRESS_PROBLEM_TOAST } from '../core/http/http-context';
import {
  decodeInstructor,
  decodeInstructorPage,
  decodeInstructorProblem,
  isSlug,
  slugFromName,
} from '../models/instructor';
import { INSTRUCTOR, toInstructorSummary } from '../models/instructor.fixtures';
import { InstructorService } from './instructor.service';

const BASE = '/api/v1/cms/instructors';

function setup() {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  return {
    instructors: TestBed.inject(InstructorService),
    http: TestBed.inject(HttpTestingController),
  };
}

describe('InstructorService', () => {
  it('lists with the filters as query parameters, and shows failures in place', async () => {
    const { instructors, http } = setup();

    const page = firstValueFrom(
      instructors.list({ cursor: 'abc', limit: 50, status: 'Published', search: 'haase' }),
    );
    const request = http.expectOne((r) => r.url === BASE);
    expect(request.request.params.get('cursor')).toBe('abc');
    expect(request.request.params.get('limit')).toBe('50');
    expect(request.request.params.get('status')).toBe('Published');
    expect(request.request.params.get('search')).toBe('haase');
    expect(request.request.context.get(SUPPRESS_PROBLEM_TOAST)).toBe(true);
    request.flush({ items: [toInstructorSummary(INSTRUCTOR)], nextCursor: null });

    expect(await page).toEqual({ items: [toInstructorSummary(INSTRUCTOR)], nextCursor: null });
  });

  it('sends an edit and a status command under the ETag it was given', async () => {
    const { instructors, http } = setup();

    const edit = firstValueFrom(instructors.update(INSTRUCTOR.id, { bio: '' }, INSTRUCTOR.etag));
    const patch = http.expectOne(`${BASE}/${INSTRUCTOR.id}`);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.headers.get('If-Match')).toBe(INSTRUCTOR.etag);
    expect(patch.request.body).toEqual({ bio: '' });
    patch.flush(INSTRUCTOR);
    expect(await edit).toEqual(INSTRUCTOR);

    const published = firstValueFrom(
      instructors.command(INSTRUCTOR.id, 'publish', INSTRUCTOR.etag),
    );
    const command = http.expectOne(`${BASE}/${INSTRUCTOR.id}/publish`);
    expect(command.request.method).toBe('POST');
    expect(command.request.headers.get('If-Match')).toBe(INSTRUCTOR.etag);
    command.flush({ ...INSTRUCTOR, status: 'Published' });
    expect((await published).status).toBe('Published');
  });

  it('refuses a body that is not an instructor', async () => {
    const { instructors, http } = setup();

    const read = firstValueFrom(instructors.get(INSTRUCTOR.id));
    http.expectOne(`${BASE}/${INSTRUCTOR.id}`).flush({ id: INSTRUCTOR.id });

    await expect(read).rejects.toThrow('not an instructor');
  });
});

describe('instructor models', () => {
  it('decodes exactly the API shape', () => {
    expect(decodeInstructor(INSTRUCTOR)).toEqual(INSTRUCTOR);
    expect(decodeInstructor({ ...INSTRUCTOR, status: 'Live' })).toBeNull();
    expect(decodeInstructor({ ...INSTRUCTOR, slugLocked: 'no' })).toBeNull();
    expect(
      decodeInstructorPage({ items: [toInstructorSummary(INSTRUCTOR)], nextCursor: 'x' })
        ?.nextCursor,
    ).toBe('x');
    expect(decodeInstructorPage({ items: [{}], nextCursor: null })).toBeNull();
  });

  it('reads the int64 legacy id as a number or a numeric string, and nothing else', () => {
    expect(decodeInstructor({ ...INSTRUCTOR, legacyWordPressId: 42 })?.legacyWordPressId).toBe(42);
    expect(decodeInstructor({ ...INSTRUCTOR, legacyWordPressId: '42' })?.legacyWordPressId).toBe(
      42,
    );
    expect(decodeInstructor({ ...INSTRUCTOR, legacyWordPressId: '4x' })).toBeNull();
    expect(decodeInstructor({ ...INSTRUCTOR, legacyWordPressId: 1.5 })).toBeNull();
  });

  it('reads field errors from a refusal', () => {
    expect(
      decodeInstructorProblem(
        { code: 'cms.slug_taken', detail: 'Taken.', errors: { slug: ['“jane” is taken.'] } },
        409,
      ),
    ).toEqual({
      status: 409,
      code: 'cms.slug_taken',
      detail: 'Taken.',
      fieldErrors: { slug: ['“jane” is taken.'] },
      correlationId: null,
    });
  });

  it.each([
    ['Candice Johnson', 'candice-johnson'],
    ["Marco Da'Vila", 'marco-da-vila'],
    ['  Socorro Muciño ', 'socorro-mucino'],
    ['—', ''],
  ])('makes the slug the API would from %j', (name, slug) => {
    expect(slugFromName(name)).toBe(slug);
  });

  it('knows a slug when it sees one', () => {
    expect(isSlug('jane-doe')).toBe(true);
    expect(isSlug('Jane-Doe')).toBe(false);
    expect(isSlug('jane--doe')).toBe(false);
    expect(isSlug('')).toBe(false);
    expect(isSlug('a'.repeat(121))).toBe(false);
  });
});
