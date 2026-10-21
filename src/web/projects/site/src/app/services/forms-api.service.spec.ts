import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { FormsApi, SubmissionAnswers } from './forms-api.service';

const ANSWERS: SubmissionAnswers = {
  artist: { name: 'Jane Rivera', email: 'jane@example.org' },
  work: {},
  wants: ['sell'],
  consent: { ownWork: true, newsletter: false },
  company: '',
};

function setup() {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  return { forms: TestBed.inject(FormsApi), http: TestBed.inject(HttpTestingController) };
}

/** Lets the antiforgery GET go out, answers it, and returns the POST that follows. */
async function afterAntiforgery(http: HttpTestingController, url: string) {
  http.expectOne('/bff/antiforgery').flush(null);
  await new Promise((resolve) => setTimeout(resolve));
  return http.expectOne(url);
}

describe('FormsApi', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('signs up through the gateway with a fresh antiforgery pair, honeypot included', async () => {
    const { forms, http } = setup();

    const sent = forms.subscribe('ana@example.org', '');
    const post = await afterAntiforgery(http, '/api/v1/forms/newsletter');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ email: 'ana@example.org', company: '' });
    post.flush(null, { status: 202, statusText: 'Accepted' });

    expect(await sent).toEqual({ ok: true });
  });

  it('sends a contact message', async () => {
    const { forms, http } = setup();

    const sent = forms.contact({
      name: 'Sam',
      email: 'sam@example.org',
      message: 'Hi',
      company: '',
    });
    (await afterAntiforgery(http, '/api/v1/forms/contact')).flush(null, {
      status: 202,
      statusText: 'Accepted',
    });

    expect(await sent).toEqual({ ok: true });
  });

  it('sends a submission’s answers as JSON first, then each picture', async () => {
    const { forms, http } = setup();
    const image = new File([new Uint8Array([1, 2, 3])], 'lake.jpg', { type: 'image/jpeg' });

    const sent = forms.submit(ANSWERS, [image]);
    const post = await afterAntiforgery(http, '/api/v1/forms/submissions');
    const body = post.request.body as FormData;
    const parts = [...body.entries()];
    expect(parts.map(([name]) => name)).toEqual(['submission', 'images']);
    expect(JSON.parse(await (parts[0][1] as Blob).text())).toEqual(ANSWERS);
    expect((parts[1][1] as File).name).toBe('lake.jpg');
    post.flush(null, { status: 202, statusText: 'Accepted' });

    expect(await sent).toEqual({ ok: true });
  });

  it('turns a refusal into words the person can act on', async () => {
    const { forms, http } = setup();

    const invalid = forms.contact({ name: 'Sam', email: 'nope', message: 'Hi', company: '' });
    (await afterAntiforgery(http, '/api/v1/forms/contact')).flush(
      { code: 'validation.failed', errors: { email: ['That email address does not look right.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    expect(await invalid).toEqual({
      ok: false,
      kind: 'invalid',
      message: 'That email address does not look right.',
    });

    const tooBig = forms.submit(ANSWERS, []);
    (await afterAntiforgery(http, '/api/v1/forms/submissions')).flush(
      { code: 'media.too_large', detail: 'Images can be at most 10 MB.' },
      { status: 413, statusText: 'Payload Too Large' },
    );
    expect(await tooBig).toEqual({
      ok: false,
      kind: 'invalid',
      message: 'Images can be at most 10 MB.',
    });

    const busy = forms.subscribe('ana@example.org', '');
    (await afterAntiforgery(http, '/api/v1/forms/newsletter')).flush(null, {
      status: 429,
      statusText: 'Too Many Requests',
    });
    expect(await busy).toEqual({ ok: false, kind: 'busy' });

    const down = forms.subscribe('ana@example.org', '');
    (await afterAntiforgery(http, '/api/v1/forms/newsletter')).flush(null, {
      status: 502,
      statusText: 'Bad Gateway',
    });
    expect(await down).toEqual({ ok: false, kind: 'failed' });
  });
});
