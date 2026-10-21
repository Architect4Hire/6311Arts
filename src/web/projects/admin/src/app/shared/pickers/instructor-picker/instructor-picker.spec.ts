import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { INSTRUCTOR, toInstructorSummary } from '../../../models/instructor.fixtures';
import { InstructorPicker, PickedInstructor } from './instructor-picker';

const BASE = '/api/v1/cms/instructors';

/** Two pickers side by side, as the class editor shows one per run. */
@Component({
  imports: [InstructorPicker],
  template: `
    <cms-instructor-picker heading="Saturday run" [(instructor)]="first" />
    <cms-instructor-picker heading="Sunday run" [(instructor)]="second" />
  `,
})
class Host {
  readonly first = signal<PickedInstructor | null>(null);
  readonly second = signal<PickedInstructor | null>({
    id: 'old',
    name: 'Retired Person',
    status: 'Archived',
  });
}

async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) {
  for (let i = 0; i < 5; i++) await Promise.resolve();
  await fixture.whenStable();
  fixture.detectChanges();
}

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

function render() {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  const root = fixture.nativeElement as HTMLElement;
  const pickers = [...root.querySelectorAll('cms-instructor-picker')] as HTMLElement[];
  return { fixture, root, pickers, http: TestBed.inject(HttpTestingController) };
}

function button(root: Element, label: string): HTMLButtonElement | undefined {
  return [...root.querySelectorAll('button')].find(
    (candidate) => text(candidate) === label || candidate.getAttribute('aria-label') === label,
  );
}

describe('InstructorPicker', () => {
  it('labels each panel by its own heading, so several on one page stay distinct', () => {
    const { pickers } = render();

    const [first, second] = pickers.map((picker) => {
      const section = picker.querySelector('section')!;
      const heading = picker.querySelector('h2')!;
      expect(section.getAttribute('aria-labelledby')).toBe(heading.id);
      return heading;
    });
    expect(text(first)).toBe('Saturday run');
    expect(text(second)).toBe('Sunday run');
    expect(first.id).not.toBe(second.id);
  });

  it('keeps an archived instructor a run already has on show, with their status', () => {
    const { pickers } = render();

    expect(text(pickers[1])).toContain('Retired Person');
    expect(text(pickers[1])).toContain('Archived');
    expect(text(pickers[0])).toContain('No instructor yet.');
  });

  it('searches after a pause, offers no archived instructor, and chooses one', async () => {
    const { fixture, pickers, http } = render();
    const search = pickers[0].querySelector<HTMLInputElement>('input[name="instructorSearch"]')!;

    vi.useFakeTimers();
    try {
      search.value = 'gw';
      search.dispatchEvent(new Event('input'));
      vi.advanceTimersByTime(300);
    } finally {
      vi.useRealTimers();
    }
    const request = http.expectOne((r) => r.url === BASE);
    expect(request.request.params.get('search')).toBe('gw');
    request.flush({
      items: [
        toInstructorSummary(INSTRUCTOR),
        {
          ...toInstructorSummary(INSTRUCTOR),
          id: 'gone',
          name: 'Gone Teacher',
          status: 'Archived',
        },
      ],
      nextCursor: null,
    });
    await settle(fixture);

    expect(button(pickers[0], 'Choose Gone Teacher')).toBeUndefined();
    button(pickers[0], `Choose ${INSTRUCTOR.name}`)!.click();
    await settle(fixture);

    expect(fixture.componentInstance.first()).toEqual({
      id: INSTRUCTOR.id,
      name: INSTRUCTOR.name,
      status: INSTRUCTOR.status,
    });
    expect(fixture.componentInstance.second()?.id).toBe('old');
  });

  it('takes the instructor off', async () => {
    const { fixture, pickers } = render();

    button(pickers[1], 'Remove Retired Person')!.click();
    await settle(fixture);

    expect(fixture.componentInstance.second()).toBeNull();
    expect(text(pickers[1])).toContain('No instructor yet.');
  });
});
