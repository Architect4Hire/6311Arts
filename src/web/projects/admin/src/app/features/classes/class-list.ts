import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
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
import { UiButton, UiControl, UiEmptyState, UiField, UiNotice, UiStatusChip } from '@6311/ui';
import { GALLERY_TIME_ZONE } from '../../core/time/gallery-time';
import { PublishStatus, STATUS_CHIPS } from '../../models/content';
import { CLASS_LIMITS, ClassListQuery, ClassSummary } from '../../models/class';
import { ClassService } from '../../services/class.service';

type Filter = 'all' | PublishStatus;

const FILTERS: readonly { readonly value: Filter; readonly label: string }[] = [
  { value: 'all', label: 'All classes' },
  { value: 'Draft', label: 'Drafts' },
  { value: 'Scheduled', label: 'Scheduled' },
  { value: 'Published', label: 'Published' },
  { value: 'Archived', label: 'Archived' },
];

/** How long typing pauses before the search is sent. */
const SEARCH_DELAY_MS = 300;

const PAGE_SIZE = 50;

function countOf(count: number, more: boolean): string {
  if (count === 0) return 'No classes to show.';
  return `${more ? 'Showing the first ' : ''}${count} ${count === 1 ? 'class' : 'classes'}.`;
}

const NEXT = new Intl.DateTimeFormat('en-US', {
  dateStyle: 'medium',
  timeStyle: 'short',
  timeZone: GALLERY_TIME_ZONE,
});

const UPDATED = new Intl.DateTimeFormat('en-US', {
  dateStyle: 'medium',
  timeZone: GALLERY_TIME_ZONE,
});

/**
 * Every class and workshop (`/admin/classes`), by title, with what it is, who teaches it, how many sessions it has
 * and when it next meets. Filter by status, search by title, and open one to edit it.
 */
@Component({
  selector: 'cms-class-list',
  imports: [RouterLink, UiButton, UiControl, UiEmptyState, UiField, UiNotice, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head">
      <div>
        <h1 class="cms-title">Classes</h1>
        <p class="cms-lead">
          Classes and workshops, their sessions and instructors. Seats and prices are Square’s
          record.
        </p>
      </div>
      <a uiButton variant="dark" routerLink="/classes/new">New class</a>
    </div>

    <div class="tools">
      <ui-field label="Find a class" class="tools__search">
        <input
          #searchInput
          uiControl
          type="search"
          name="search"
          autocomplete="off"
          [attr.maxlength]="limits.searchMaxLength"
          [value]="search()"
          (input)="onSearch(searchInput.value)"
        />
      </ui-field>
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
    </div>

    <!-- Exists from the start, so the counts written into it later are read out. -->
    <p class="sr-only" aria-live="polite">{{ liveMessage() }}</p>

    @switch (state()) {
      @case ('loading') {
        <p role="status">Loading classes…</p>
      }
      @case ('failed') {
        <ui-notice tone="error" heading="The classes couldn’t be loaded">
          <p>6311arts may be unreachable for a moment.</p>
          <button uiButton type="button" size="sm" (click)="reload()">Try again</button>
        </ui-notice>
      }
      @default {
        @if (items().length === 0) {
          <ui-empty-state icon="calendar" [heading]="emptyHeading()">
            <p>{{ emptyText() }}</p>
          </ui-empty-state>
        } @else {
          @if (refreshing()) {
            <p role="status" class="refreshing">Updating the list…</p>
          }
          <div class="scroll" role="region" aria-label="Classes" tabindex="0">
            <table class="classes" [attr.aria-busy]="refreshing() || null">
              <caption class="sr-only">
                Classes, by title
              </caption>
              <thead>
                <tr>
                  <th scope="col">Title</th>
                  <th scope="col">Next session</th>
                  <th scope="col">Status</th>
                  <th scope="col" class="wide">Sessions</th>
                  <th scope="col" class="wide">Instructor</th>
                  <th scope="col" class="wide">Updated</th>
                </tr>
              </thead>
              <tbody>
                @for (offering of items(); track offering.id; let index = $index) {
                  <tr>
                    <th scope="row">
                      <a [routerLink]="['/classes', offering.id]" [attr.data-row]="index">{{
                        offering.title
                      }}</a>
                      <span class="slug">/classes/{{ offering.slug }}</span>
                      @if (offering.classType || offering.level) {
                        <span class="kind">{{ kind(offering) }}</span>
                      }
                    </th>
                    <td>{{ next(offering.nextSessionStartsAt) }}</td>
                    <td>
                      <ui-status-chip
                        [tone]="chips[offering.status].tone"
                        [icon]="chips[offering.status].icon"
                        >{{ chips[offering.status].label }}</ui-status-chip
                      >
                    </td>
                    <td class="wide">{{ offering.sessionCount }}</td>
                    <td class="wide">{{ offering.instructor || '—' }}</td>
                    <td class="wide">
                      <time [attr.datetime]="offering.updatedAt">{{
                        updated(offering.updatedAt)
                      }}</time>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          @if (moreFailed()) {
            <ui-notice tone="error" heading="More classes couldn’t be loaded">
              <p>The classes above are still here.</p>
            </ui-notice>
          }
          @if (nextCursor()) {
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
    .head {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-4);
      align-items: flex-end;
      justify-content: space-between;
    }
    .cms-lead {
      margin-bottom: 0;
    }
    .tools {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-4);
      align-items: flex-end;
    }
    .tools__search {
      flex: 1 1 260px;
      max-width: 420px;
    }
    .filters {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
    .scroll {
      overflow-x: auto;
    }
    .classes {
      width: 100%;
      border-collapse: collapse;
    }
    .classes th,
    .classes td {
      padding: var(--space-2) var(--space-3);
      border-bottom: var(--border-thin) solid var(--line);
      text-align: left;
      vertical-align: top;
    }
    .classes thead th {
      font-size: var(--fs-label);
      font-weight: var(--fw-heavy);
      letter-spacing: var(--track-label);
      text-transform: uppercase;
    }
    .classes tbody th {
      font-weight: var(--fw-bold);
    }
    .slug,
    .kind {
      display: block;
      font-size: var(--fs-small);
      font-weight: normal;
      color: var(--text-soft);
      overflow-wrap: anywhere;
    }
    .more {
      text-align: center;
    }
    .refreshing {
      margin: 0;
      color: var(--text-soft);
    }
    /* On a phone, the counts, instructor and date go, so nothing hides off to the side. */
    @media (max-width: 600px) {
      .wide {
        display: none;
      }
    }
  `,
})
export class ClassList {
  private readonly classes = inject(ClassService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  /** Bumped by every new list request, so an answer to an older one (a filter since changed) is dropped. */
  private generation = 0;

  protected readonly filters = FILTERS;
  protected readonly chips = STATUS_CHIPS;
  protected readonly limits = CLASS_LIMITS;
  protected readonly filter = signal<Filter>('all');
  protected readonly search = signal('');
  /** The search as last sent: `search` settles into it after a pause in typing. */
  private readonly query = signal('');
  protected readonly state = signal<'loading' | 'failed' | 'ready'>('loading');
  protected readonly items = signal<readonly ClassSummary[]>([]);
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly loadingMore = signal(false);
  protected readonly moreFailed = signal(false);
  /** A new filter or search is loading over rows already shown: they stay put rather than vanishing. */
  protected readonly refreshing = signal(false);
  protected readonly liveMessage = signal('');

  constructor() {
    effect(() => {
      this.filter();
      this.query();
      untracked(() => void this.reload());
    });
    inject(DestroyRef).onDestroy(() => this.clearSearchTimer());
  }

  protected emptyHeading(): string {
    return this.filter() === 'all' && this.query() === ''
      ? 'No classes yet'
      : 'Nothing to show here';
  }

  protected emptyText(): string {
    if (this.query() !== '') return `No class’s title contains “${this.query()}”.`;
    return this.filter() === 'all'
      ? 'Add the first class with “New class”.'
      : `No class is ${this.filter().toLowerCase()}.`;
  }

  protected kind(offering: ClassSummary): string {
    return [offering.classType, offering.level].filter(Boolean).join(' · ');
  }

  protected next(iso: string | null): string {
    if (iso === null) return 'No sessions to come';
    const date = new Date(iso);
    return Number.isNaN(date.getTime()) ? '—' : NEXT.format(date);
  }

  protected updated(iso: string): string {
    const date = new Date(iso);
    return Number.isNaN(date.getTime()) ? '—' : UPDATED.format(date);
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    this.clearSearchTimer();
    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      this.query.set(value.trim());
    }, SEARCH_DELAY_MS);
  }

  async reload(): Promise<void> {
    const generation = ++this.generation;
    const showing = this.state() === 'ready' && this.items().length > 0;
    if (showing) this.refreshing.set(true);
    else this.state.set('loading');
    this.moreFailed.set(false);
    try {
      const page = await firstValueFrom(this.classes.list(this.currentQuery()));
      if (generation !== this.generation) return;
      this.items.set(page.items);
      this.nextCursor.set(page.nextCursor);
      this.state.set('ready');
      this.liveMessage.set(countOf(page.items.length, page.nextCursor !== null));
    } catch {
      if (generation === this.generation) this.state.set('failed');
    } finally {
      if (generation === this.generation) this.refreshing.set(false);
    }
  }

  protected async more(): Promise<void> {
    const cursor = this.nextCursor();
    if (!cursor || this.loadingMore()) return;

    const generation = this.generation;
    this.loadingMore.set(true);
    this.moreFailed.set(false);
    try {
      const page = await firstValueFrom(this.classes.list({ ...this.currentQuery(), cursor }));
      if (generation !== this.generation) return;
      const firstNew = this.items().length;
      this.items.update((items) => [...items, ...page.items]);
      this.nextCursor.set(page.nextCursor);
      this.liveMessage.set(
        `${page.items.length} more ${page.items.length === 1 ? 'class' : 'classes'} loaded.`,
      );
      // "Load more" may be gone now; focus goes to the first class added, where reading continues.
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

  private currentQuery(): ClassListQuery {
    const filter = this.filter();
    const search = this.query();
    return {
      limit: PAGE_SIZE,
      ...(filter === 'all' ? {} : { status: filter }),
      ...(search ? { search } : {}),
    };
  }

  private clearSearchTimer(): void {
    if (this.searchTimer !== null) clearTimeout(this.searchTimer);
    this.searchTimer = null;
  }
}
