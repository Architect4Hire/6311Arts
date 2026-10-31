import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import { ConfirmRequest, ConfirmService } from '../core/confirm/confirm.service';
import { ApiProblem } from '../models/decode';
import {
  ContentLifecycle,
  ContentLifecycleApi,
  LifecycleCopy,
  LifecycleRecord,
  statusTextOf,
} from './content-lifecycle';

interface Tier extends LifecycleRecord {
  readonly name: string;
}

const DRAFT: Tier = {
  id: 'tier-1',
  etag: 'W/"1"',
  slug: 'friend',
  name: 'Friend',
  status: 'Draft',
  publishedAt: null,
  scheduledFor: null,
};

const COPY: LifecycleCopy = {
  noun: 'tier',
  deleteNoun: 'plan',
  keepLabel: 'Keep it',
  inSquare: true,
  previewType: 'membership-plan',
  unpublishMessage: 'It leaves the memberships page.',
  archiveMessage: 'An archived tier is retired.',
  deleteText: 'The plan leaves the site.',
  published: 'Published: on the memberships page.',
};

function refusal(status: number, code: string, detail: string | null = null, errors = {}) {
  return throwError(
    () => new HttpErrorResponse({ status, error: { title: 'Refused', code, detail, errors } }),
  );
}

/** A host editor: its record, form state and everything the lifecycle told it. */
class Host {
  record: Tier | null = DRAFT;
  dirty = false;
  saving = false;
  readonly announced: string[] = [];
  readonly refusals: { message: string; problem: ApiProblem | null }[] = [];
  readonly asked: ConfirmRequest[] = [];
  readonly calls: string[] = [];
  answers: boolean[] = [];
  deletedWhileBusy: boolean | null = null;

  api: ContentLifecycleApi<Tier> = {
    get: (id) => {
      this.calls.push(`get ${id}`);
      return of({ ...DRAFT, etag: 'W/"9"', status: 'Published' });
    },
    command: (id, command, etag) => {
      this.calls.push(`${command} ${id} ${etag}`);
      return of({ ...DRAFT, etag: 'W/"2"', status: command === 'publish' ? 'Published' : 'Draft' });
    },
    schedule: (id, at, etag) => {
      this.calls.push(`schedule ${id} ${at} ${etag}`);
      return of({ ...DRAFT, status: 'Scheduled', scheduledFor: at });
    },
    delete: (id, etag) => {
      this.calls.push(`delete ${id} ${etag}`);
      return of(undefined);
    },
  };

  readonly lifecycle: ContentLifecycle<Tier>;

  constructor() {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: ConfirmService,
          useValue: {
            confirm: (request: ConfirmRequest) => {
              this.asked.push(request);
              return Promise.resolve(this.answers[this.asked.length - 1] ?? false);
            },
          },
        },
      ],
    });
    this.lifecycle = TestBed.runInInjectionContext(
      () =>
        new ContentLifecycle<Tier>({
          copy: COPY,
          api: {
            get: (id) => this.api.get(id),
            command: (id, command, etag) => this.api.command(id, command, etag),
            schedule: (id, at, etag) => this.api.schedule(id, at, etag),
            delete: (id, etag) => this.api.delete(id, etag),
          },
          record: () => this.record,
          label: (tier) => tier.name,
          dirty: () => this.dirty,
          busy: () => this.saving || this.lifecycle.busy(),
          replace: (tier) => (this.record = tier),
          clearErrors: () => this.calls.push('clear'),
          refused: (message, problem) => this.refusals.push({ message, problem }),
          announce: (message) => this.announced.push(message),
          deleted: () => {
            this.deletedWhileBusy = this.lifecycle.busy();
            return Promise.resolve(true);
          },
        }),
    );
  }
}

describe('ContentLifecycle', () => {
  it('publishes under the saved row version and shows the answer', async () => {
    const host = new Host();
    const answer = new Subject<Tier>();
    host.api = { ...host.api, command: () => answer as Observable<Tier> };

    const done = host.lifecycle.publish();
    expect(host.lifecycle.commanding()).toBe('publish');
    expect(host.lifecycle.busy()).toBe(true);

    answer.next({ ...DRAFT, status: 'Published' });
    answer.complete();

    expect(await done).toBe(true);
    expect(host.record?.status).toBe('Published');
    expect(host.lifecycle.busy()).toBe(false);
    expect(host.announced).toEqual(['Publishing…', 'Published.']);
  });

  it('waits for a save: no command while the form is dirty or saving', async () => {
    const host = new Host();
    host.dirty = true;
    expect(await host.lifecycle.publish()).toBe(false);

    host.dirty = false;
    host.saving = true;
    expect(await host.lifecycle.unschedule()).toBe(false);

    expect(host.calls).toEqual([]);
  });

  it('asks before unpublishing, in the type’s own words, and does nothing on "keep"', async () => {
    const host = new Host();
    host.record = { ...DRAFT, status: 'Published' };

    expect(await host.lifecycle.unpublish()).toBe(false);

    expect(host.asked[0]).toMatchObject({
      heading: 'Unpublish this tier?',
      message: 'It leaves the memberships page.',
      cancelLabel: 'Keep published',
    });
    expect(host.calls).toEqual([]);
  });

  it('archives once confirmed, and says the record is a draft again after an unschedule', async () => {
    const host = new Host();
    host.answers = [true];

    expect(await host.lifecycle.archive()).toBe(true);
    expect(host.asked[0]).toMatchObject({ confirmLabel: 'Archive tier', cancelLabel: 'Keep it' });
    expect(host.calls).toContain('archive tier-1 W/"1"');

    await host.lifecycle.unschedule();
    expect(host.announced.at(-1)).toBe('Unscheduled. The tier is a draft again.');
  });

  it('schedules with its body', async () => {
    const host = new Host();

    expect(await host.lifecycle.schedule('2026-11-02T15:00:00Z')).toBe(true);
    expect(host.calls).toContain('schedule tier-1 2026-11-02T15:00:00Z W/"1"');
    expect(host.record?.status).toBe('Scheduled');
  });

  it('shows the latest version after a conflict, and says so', async () => {
    const host = new Host();
    host.api = { ...host.api, command: () => refusal(409, 'concurrency.conflict') };

    expect(await host.lifecycle.publish()).toBe(false);

    expect(host.calls).toContain('get tier-1');
    expect(host.record?.etag).toBe('W/"9"');
    expect(host.refusals[0].message).toBe(
      'Someone else changed this tier a moment ago. Here is the latest version; check it and try again.',
    );
    expect(host.lifecycle.busy()).toBe(false);
  });

  it('hands the form what publishing still needs, without a refetch', async () => {
    const host = new Host();
    host.api = {
      ...host.api,
      command: () =>
        refusal(422, 'validation.failed', 'Add a perk first.', {
          perks: ['Add at least one perk.'],
        }),
    };

    await host.lifecycle.publish();

    expect(host.calls).not.toContain('get tier-1');
    expect(host.refusals[0].message).toBe('Add a perk first.');
    expect(host.refusals[0].problem?.fieldErrors['perks']).toEqual(['Add at least one perk.']);
  });

  it('deletes by the delete noun, asks Square’s question, and is not busy when it hands over', async () => {
    const host = new Host();
    host.dirty = true;
    host.answers = [true, true];

    await host.lifecycle.remove();

    expect(host.asked.map((request) => request.heading)).toEqual([
      'Delete this plan?',
      'This also deletes it from Square',
    ]);
    expect(host.calls).toContain('delete tier-1 W/"1"');
    expect(host.announced).toContain('Deleting…');
    expect(host.deletedWhileBusy).toBe(false);
  });

  it('shows why a delete was refused as the API said it, with no field errors', async () => {
    const host = new Host();
    host.answers = [true, true];
    host.api = {
      ...host.api,
      delete: () => refusal(409, 'cms.in_use', 'The Spring show still credits this plan.'),
    };

    await host.lifecycle.remove();

    expect(host.refusals).toEqual([
      { message: 'The Spring show still credits this plan.', problem: null },
    ]);
    expect(host.lifecycle.deleting()).toBe(false);
  });
});

describe('statusTextOf', () => {
  it('uses the type’s own words where it has them, and the common ones otherwise', () => {
    expect(statusTextOf({ ...DRAFT, status: 'Published' }, COPY)).toBe(
      'Published: on the memberships page.',
    );
    expect(statusTextOf({ ...DRAFT, status: 'Archived' }, COPY)).toBe(
      'Archived: retired from the site. Archiving can’t be undone here.',
    );
    expect(statusTextOf({ ...DRAFT, status: 'Scheduled' }, COPY)).toBe('Scheduled to publish.');
    expect(statusTextOf(DRAFT, COPY)).toBe('A draft: only staff can see it.');
    expect(statusTextOf({ ...DRAFT, publishedAt: '2026-10-01T00:00:00Z' }, COPY)).toBe(
      'A draft again, after being published. Its web address stays fixed.',
    );
  });
});
