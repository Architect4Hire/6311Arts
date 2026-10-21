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
} from '../../models/forms';
import { FormsService } from '../../services/forms.service';
import { FormsTabs, formatInstant } from './forms-tabs';

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
  template: `
    <cms-forms-tabs />

    <div class="filters" role="group" aria-label="Filter by status">
      @for (option of filters; track option.value) {
        <button
          uiButton
          type="button"
          size="sm"
          [variant]="filter() === option.value ? 'dark' : 'border-dark'"
          [attr.aria-pressed]="filter() === option.value"
          (click)="filter.set(option.value)"
        >
          {{ option.label }}
        </button>
      }
    </div>

    <p class="sr-only" aria-live="polite">{{ liveMessage() }}</p>

    @switch (state()) {
      @case ('loading') {
        <p role="status">Loading submissions…</p>
      }
      @case ('failed') {
        <ui-notice tone="error" heading="The submissions couldn’t be loaded">
          <p>6311arts may be unreachable for a moment.</p>
          <button uiButton type="button" size="sm" (click)="reload()">Try again</button>
        </ui-notice>
      }
      @default {
        @if (items().length === 0) {
          <ui-empty-state
            icon="mail"
            [heading]="filter() === 'all' ? 'No submissions yet' : 'Nothing to show here'"
          >
            <p>
              {{
                filter() === 'all'
                  ? 'Artists’ submissions from the site’s form arrive here.'
                  : 'No submissions have this status.'
              }}
            </p>
          </ui-empty-state>
        } @else {
          <div class="scroll" role="region" aria-label="Submissions" tabindex="0">
            <table class="list" [attr.aria-busy]="refreshing() || null">
              <caption class="sr-only">
                Artist submissions, newest first
              </caption>
              <thead>
                <tr>
                  <th scope="col">Name</th>
                  <th scope="col" class="wide">Email</th>
                  <th scope="col">Wants to</th>
                  <th scope="col">Status</th>
                  <th scope="col" class="wide">Pictures</th>
                  <th scope="col" class="wide">Received</th>
                </tr>
              </thead>
              <tbody>
                @for (submission of items(); track submission.id; let index = $index) {
                  <tr>
                    <th scope="row">
                      <a
                        [routerLink]="['/forms/submissions', submission.id]"
                        [attr.data-row]="index"
                        >{{ submission.name }}</a
                      >
                      <span class="narrow">{{ submission.email }} · {{ received(submission.receivedAt) }}</span>
                    </th>
                    <td class="wide text">{{ submission.email }}</td>
                    <td>{{ wants(submission) }}</td>
                    <td>
                      <ui-status-chip
                        [tone]="chips[submission.status].tone"
                        [icon]="chips[submission.status].icon"
                        >{{ chips[submission.status].label }}</ui-status-chip
                      >
                    </td>
                    <td class="wide">{{ submission.imageCount }}</td>
                    <td class="wide">
                      <time [attr.datetime]="submission.receivedAt">{{
                        received(submission.receivedAt)
                      }}</time>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          @if (moreFailed()) {
            <ui-notice tone="error" heading="More submissions couldn’t be loaded">
              <p>The submissions above are still here.</p>
            </ui-notice>
          }
          @if (next()) {
            <p class="more">
              <button uiButton type="button" [busy]="loadingMore()" (click)="more()">
                {{ moreFailed() ? 'Try loading more again' : 'Load more' }}
              </button>
            </p>
          }
        }
      }
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-5);
    }
    .filters {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
    .scroll {
      overflow-x: auto;
    }
    .list {
      width: 100%;
      border-collapse: collapse;
    }
    .list th,
    .list td {
      padding: var(--space-2) var(--space-3);
      border-bottom: var(--border-thin) solid var(--line);
      text-align: left;
      vertical-align: top;
    }
    .list thead th {
      font-size: var(--fs-label);
      font-weight: var(--fw-heavy);
      letter-spacing: var(--track-label);
      text-transform: uppercase;
    }
    .list tbody th,
    .text {
      overflow-wrap: anywhere;
    }
    .more {
      text-align: center;
    }
    .narrow {
      display: none;
      color: var(--muted);
      font-size: var(--fs-small);
      font-weight: var(--fw-regular);
    }
    @media (max-width: 600px) {
      .wide {
        display: none;
      }
      .narrow {
        display: block;
      }
    }
  `,
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
