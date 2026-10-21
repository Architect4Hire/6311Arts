import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { FormOutcome, FormsApi, SubmissionAnswers } from '../services/forms-api.service';
import { ArtistSubmission } from './artist-submission';

async function setup(outcome: FormOutcome = { ok: true }) {
  vi.stubGlobal('matchMedia', () => ({
    matches: true,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
  const submit = vi.fn(async (_answers: SubmissionAnswers, _images: readonly File[]) => outcome);
  TestBed.configureTestingModule({
    providers: [
      provideRouter([{ path: 'artists/submit', component: ArtistSubmission }]),
      { provide: FormsApi, useValue: { submit } },
    ],
  });
  const harness = await RouterTestingHarness.create();
  const page = await harness.navigateByUrl('/artists/submit', ArtistSubmission);
  const element = harness.routeNativeElement!;
  const set = async (name: string, value: string | boolean) => {
    const input = element.querySelector<HTMLInputElement>(`[name="${name}"]`)!;
    if (typeof value === 'boolean') input.checked = value;
    else input.value = value;
    input.dispatchEvent(new Event(typeof value === 'boolean' ? 'change' : 'input'));
    await harness.fixture.whenStable();
  };
  const choose = async (files: File[]) => {
    const input = element.querySelector<HTMLInputElement>('input[type="file"]')!;
    Object.defineProperty(input, 'files', { value: files, configurable: true });
    input.dispatchEvent(new Event('change'));
    await harness.fixture.whenStable();
  };
  const send = async () => {
    element.querySelector<HTMLFormElement>('form.sform')!.dispatchEvent(new Event('submit'));
    await harness.fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve));
    await harness.fixture.whenStable();
  };
  const goal = async (label: string) => {
    const button = [...element.querySelectorAll<HTMLElement>('button, label')].find(
      (b) => b.textContent?.trim() === label,
    )!;
    button.click();
    await harness.fixture.whenStable();
  };
  return { harness, page, element, submit, set, choose, send, goal };
}

async function fillRequired(s: Awaited<ReturnType<typeof setup>>) {
  await s.set('name', 'Jane Rivera');
  await s.set('email', 'jane@example.org');
  await s.goal('Sell in the shop');
  await s.set('ownWork', true);
}

describe('ArtistSubmission', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('no longer says it is a draft that sends nothing', async () => {
    const { element } = await setup();
    expect(element.textContent).not.toContain('Draft form');
    expect(element.textContent).not.toContain('stub');
  });

  it('sends the answers and the pictures, then thanks the artist', async () => {
    const s = await setup();
    await fillRequired(s);
    const image = new File([new Uint8Array(10)], 'lake.jpg', { type: 'image/jpeg' });
    await s.choose([image]);

    await s.send();

    expect(s.submit).toHaveBeenCalledTimes(1);
    const [answers, images] = s.submit.mock.calls[0];
    expect(answers.artist).toMatchObject({ name: 'Jane Rivera', email: 'jane@example.org' });
    expect(answers.wants).toEqual(['sell']);
    expect(answers.sell).toBeDefined();
    expect(answers.teach).toBeUndefined();
    expect(images).toEqual([image]);
    expect(s.element.querySelector('.sform__done')?.textContent).toContain(
      'Your submission is with us',
    );
  });

  it('stops seven pictures, or one over 10 MB, before anything is sent', async () => {
    const s = await setup();
    await fillRequired(s);

    await s.choose(
      Array.from(
        { length: 7 },
        (_, i) => new File([new Uint8Array(1)], `p${i}.jpg`, { type: 'image/jpeg' }),
      ),
    );
    await s.send();
    expect(s.element.querySelector('[role="alert"]')?.textContent).toContain('up to 6 images');

    await s.choose([
      new File([new Uint8Array(10 * 1024 * 1024 + 1)], 'huge.jpg', { type: 'image/jpeg' }),
    ]);
    await s.send();
    expect(s.element.querySelector('[role="alert"]')?.textContent).toContain(
      'huge.jpg is larger than 10 MB',
    );
    expect(s.submit).not.toHaveBeenCalled();
  });

  it('shows the API’s reason when the submission is refused, and keeps the form', async () => {
    const s = await setup({
      ok: false,
      kind: 'invalid',
      message: 'Use a full web address, starting with https://.',
    });
    await fillRequired(s);

    await s.send();

    expect(s.element.querySelector('[role="alert"]')?.textContent).toContain(
      'Use a full web address',
    );
    expect(s.element.querySelector('form.sform')).not.toBeNull();
  });
});
