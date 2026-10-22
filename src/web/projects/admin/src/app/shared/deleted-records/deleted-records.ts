import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import { UiButton, UiEmptyState, UiNotice, UiStatusChip } from '@6311/ui';
import { ConfirmService } from '../../core/confirm/confirm.service';
import { SessionService } from '../../core/session/session.service';
import { PublishStatus, STATUS_CHIPS } from '../../models/content';
import { ROLES } from '../../models/session';

/** One deleted record, as every type's Deleted view shows it (`*DeletedServiceModel`, ADR 0052). */
export interface DeletedRecord {
  readonly id: string;
  /** What staff call it: a title or a name, whichever the type uses. */
  readonly label: string;
  readonly slug: string;
  /** What it was when it was deleted, not where it is now. */
  readonly status: PublishStatus;
  readonly deletedAt: string;
  /** The row version, sent back as `If-Match` when restoring it. */
  readonly etag: string;
}

export interface DeletedRecordsPage {
  readonly items: readonly DeletedRecord[];
  readonly nextCursor: string | null;
}

/**
 * Where a Deleted view gets its rows and how it puts one back: the type's own service, adapted to
 * {@link DeletedRecord} by the screen that owns it.
 */
export interface DeletedRecordsSource {
  list(query: {
    readonly cursor?: string | null;
    readonly search?: string;
  }): Observable<DeletedRecordsPage>;
  restore(id: string, etag: string): Observable<unknown>;
}

const DELETED = new Intl.DateTimeFormat('en-US', {
  dateStyle: 'medium',
  timeZone: 'America/Chicago',
});

/**
 * The Deleted view every content type shares (ADR 0052): what staff deleted, what it was when it went, and an
 * Admin-only **Restore** that brings one back as a draft. One component rather than one per type, because the rows
 * and the actions are the same everywhere; only where they come from differs.
 */
@Component({
  selector: 'cms-deleted-records',
  imports: [UiButton, UiEmptyState, UiNotice, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './deleted-records.html',
  styleUrl: './deleted-records.scss',
})
export class DeletedRecords {
  /** The type, as a sentence names one: "page", "artist". */
  readonly noun = input.required<string>();

  /** The same, plural: "pages", "artists". */
  readonly plural = input.required<string>();

  readonly source = input.required<DeletedRecordsSource>();

  /** Part of a name to match, from the screen's own search box. */
  readonly search = input('');

  private readonly confirm = inject(ConfirmService);
  private readonly session = inject(SessionService);
  private readonly injector = inject(Injector);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  protected readonly chips = STATUS_CHIPS;
  protected readonly state = signal<'loading' | 'failed' | 'ready'>('loading');
  protected readonly items = signal<readonly DeletedRecord[]>([]);
  protected readonly next = signal<string | null>(null);
  protected readonly loadingMore = signal(false);
  protected readonly restoring = signal<string | null>(null);
  protected readonly failure = signal<string | null>(null);
  protected readonly liveMessage = signal('');

  /** Restoring is the Admin's, as deleting was. Everyone who reaches this view can read it. */
  protected readonly canRestore = computed(() => this.session.hasAnyRole([ROLES.admin]));

  /** Bumped by every new request, so an answer to an older one is dropped. */
  private generation = 0;

  constructor() {
    effect(() => {
      this.source();
      this.search();
      untracked(() => void this.reload());
    });
  }

  async reload(): Promise<void> {
    const generation = ++this.generation;
    this.state.set('loading');
    this.failure.set(null);
    try {
      const page = await firstValueFrom(this.source().list({ search: this.search() || undefined }));
      if (generation !== this.generation) return;
      this.items.set(page.items);
      this.next.set(page.nextCursor);
      this.state.set('ready');
      this.announce(this.countOf(page.items.length));
    } catch {
      if (generation === this.generation) this.state.set('failed');
    }
  }

  protected async more(): Promise<void> {
    const cursor = this.next();
    if (!cursor || this.loadingMore()) return;

    const generation = this.generation;
    this.loadingMore.set(true);
    try {
      const page = await firstValueFrom(
        this.source().list({ cursor, search: this.search() || undefined }),
      );
      if (generation !== this.generation) return;
      this.items.update((items) => [...items, ...page.items]);
      this.next.set(page.nextCursor);
      this.announce(`${page.items.length} more loaded. ${this.countOf(this.items().length)}`);
    } catch {
      if (generation === this.generation) {
        this.failure.set('More couldn’t be loaded. The rows above are still here.');
      }
    } finally {
      this.loadingMore.set(false);
    }
  }

  protected async restore(row: DeletedRecord): Promise<void> {
    if (this.restoring() !== null) return;

    const confirmed = await this.confirm.confirm({
      heading: `Restore “${row.label}”?`,
      message: `It comes back as a draft at /${row.slug}, so nothing returns to the site until you publish it again.`,
      confirmLabel: `Restore ${this.noun()}`,
      cancelLabel: 'Leave it deleted',
    });
    if (!confirmed) return;

    this.restoring.set(row.id);
    this.failure.set(null);
    try {
      await firstValueFrom(this.source().restore(row.id, row.etag));
      await this.reload();

      // After the reload, not before: reload() writes the new count into the same live region, which would
      // otherwise swallow this (WCAG 4.1.3).
      this.announce(`“${row.label}” is a draft again. ${this.countOf(this.items().length)}`);

      // The row that held focus has gone with the reload, so put it somewhere deliberate rather than on <body>
      // (WCAG 2.4.3).
      this.focusStatus();
    } catch {
      this.failure.set(
        `“${row.label}” couldn’t be restored. Another ${this.noun()} may have taken /${row.slug}, or someone else changed it — reload and try again.`,
      );
    } finally {
      this.restoring.set(null);
    }
  }

  /** Says it again rather than not at all: an identical message would not be re-read. */
  private announce(message: string): void {
    this.liveMessage.update((current) => (current === message ? `${message} ` : message));
  }

  private focusStatus(): void {
    afterNextRender(() => this.heading()?.nativeElement.focus(), { injector: this.injector });
  }

  protected deleted(iso: string): string {
    const date = new Date(iso);
    return Number.isNaN(date.getTime()) ? '—' : DELETED.format(date);
  }

  /** What "nothing here" means depends on whether a search is narrowing it (W2). */
  protected readonly emptyText = computed(() =>
    this.search() === ''
      ? `No ${this.noun()} has been deleted.`
      : `No deleted ${this.noun()}’s name contains “${this.search()}”.`,
  );

  private countOf(count: number): string {
    return count === 0
      ? `No deleted ${this.plural()}.`
      : `${count} deleted ${count === 1 ? this.noun() : this.plural()}.`;
  }
}
