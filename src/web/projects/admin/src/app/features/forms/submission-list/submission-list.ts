import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiEmptyState, UiNotice, UiStatusChip } from '@6311/ui';
import {
  SUBMISSION_CHIPS,
  SubmissionListQuery,
  SubmissionStatus,
  SubmissionSummary,
  wantLabel,
} from '../../../models/forms';
import { FormsService } from '../../../services/forms.service';
import { FormsTabs, formatInstant } from '../forms-tabs/forms-tabs';

type Filter = 'all' | SubmissionStatus;

const FILTERS: readonly { readonly value: Filter; readonly label: string }[] = [
  { value: 'all', label: 'All' },
  { value: 'New', label: 'New' },
  { value: 'Reviewed', label: 'Reviewed' },
  { value: 'Accepted', label: 'Accepted' },
  { value: 'Declined', label: 'Declined' },
];

const PAGE_SIZE = 50;

function countOf(count: number, more: boolean): string {
  if (count === 0) return 'No submissions to show.';
  return `${more ? 'Showing the first ' : ''}${count} ${count === 1 ? 'submission' : 'submissions'}.`;
}

/**
 * The artist submissions inbox (`/admin/forms/submissions`), newest first: who wrote, what they want, how far
 * through the inbox each is, and how many pictures came with it. Filter by status and open one to read it.
 */
@Component({
  selector: 'cms-submission-list',
  imports: [FormsTabs, RouterLink, UiButton, UiEmptyState, UiNotice, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './submission-list.html',
  styleUrl: './submission-list.scss',
})
export class SubmissionList {
  private readonly forms = inject(FormsService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private generation = 0;

  protected readonly filters = FILTERS;
  protected readonly chips = SUBMISSION_CHIPS;
  protected readonly filter = signal<Filter>('all');
  protected readonly state = signal<'loading' | 'failed' | 'ready'>('loading');
  protected readonly items = signal<readonly SubmissionSummary[]>([]);
  protected readonly next = signal<string | null>(null);
  protected readonly loadingMore = signal(false);
  protected readonly moreFailed = signal(false);
  protected readonly refreshing = signal(false);
  protected readonly liveMessage = signal('');

  constructor() {
    effect(() => {
      this.filter();
      untracked(() => void this.reload());
    });
  }

  protected wants(submission: SubmissionSummary): string {
    return submission.wants.length === 0 ? '—' : submission.wants.map(wantLabel).join(', ');
  }

  protected received(iso: string): string {
    return formatInstant(iso);
  }

  async reload(): Promise<void> {
    const generation = ++this.generation;
    const showing = this.state() === 'ready' && this.items().length > 0;
    if (showing) this.refreshing.set(true);
    else this.state.set('loading');
    this.moreFailed.set(false);
    try {
      const page = await firstValueFrom(this.forms.listSubmissions(this.currentQuery()));
      if (generation !== this.generation) return;
      this.items.set(page.items);
      this.next.set(page.nextCursor);
      this.state.set('ready');
      this.liveMessage.set(countOf(page.items.length, page.nextCursor !== null));
    } catch {
      if (generation === this.generation) this.state.set('failed');
    } finally {
      if (generation === this.generation) this.refreshing.set(false);
    }
  }

  protected async more(): Promise<void> {
    const cursor = this.next();
    if (!cursor || this.loadingMore()) return;

    const generation = this.generation;
    this.loadingMore.set(true);
    this.moreFailed.set(false);
    try {
      const page = await firstValueFrom(
        this.forms.listSubmissions({ ...this.currentQuery(), cursor }),
      );
      if (generation !== this.generation) return;
      const firstNew = this.items().length;
      this.items.update((items) => [...items, ...page.items]);
      this.next.set(page.nextCursor);
      this.liveMessage.set(
        `${page.items.length} more ${page.items.length === 1 ? 'submission' : 'submissions'} loaded.`,
      );
      afterNextRender(
        () =>
          this.host.nativeElement.querySelector<HTMLElement>(`a[data-row="${firstNew}"]`)?.focus(),
        { injector: this.injector },
      );
    } catch {
      if (generation === this.generation) this.moreFailed.set(true);
    } finally {
      this.loadingMore.set(false);
    }
  }

  private currentQuery(): SubmissionListQuery {
    const filter = this.filter();
    return { limit: PAGE_SIZE, ...(filter === 'all' ? {} : { status: filter }) };
  }
}
