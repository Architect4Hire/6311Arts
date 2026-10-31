import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiControl, UiEmptyState, UiField, UiNotice } from '@6311/ui';
import { BrowserDownload } from '../../../core/browser/browser-download';
import { ConfirmService } from '../../../core/confirm/confirm.service';
import { SessionService } from '../../../core/session/session.service';
import {
  FORMS_CODES,
  FORMS_LIMITS,
  FormsProblem,
  NEWSLETTER_SOURCE_LABELS,
  NewsletterListQuery,
  NewsletterSubscriber,
  decodeFormsProblem,
} from '../../../models/forms';
import { ROLES } from '../../../models/session';
import { FormsService } from '../../../services/forms.service';
import { FormsTabs, formatInstant } from '../forms-tabs/forms-tabs';
import { ListSort, SortColumn } from '../../../shared/list-sort';
import { SortHeader } from '../../../shared/sort-header/sort-header';
import { SortPicker } from '../../../shared/sort-picker/sort-picker';
import { Sortable } from '../../../shared/sortable';

/** How long typing pauses before the search is sent. */
const SEARCH_DELAY_MS = 300;

const PAGE_SIZE = 50;

function countOf(count: number, more: boolean): string {
  if (count === 0) return 'No addresses to show.';
  return `${more ? 'Showing the first ' : ''}${count} ${count === 1 ? 'address' : 'addresses'}.`;
}

/** The columns a header sorts by (ADR 0062); the API names the same ones. */
const SORTS: readonly SortColumn[] = [
  { key: 'email', label: 'Email', kind: 'text' },
  { key: 'consented', label: 'Consented', kind: 'date' },
  { key: 'wording', label: 'Consent wording', kind: 'text' },
  {
    key: 'source',
    label: 'Source',
    kind: 'status',
    words: { asc: 'sign-ups first', desc: 'submissions first' },
  },
];

/**
 * The newsletter list (`/admin/forms/newsletter`), newest first: each address, when it agreed and to which wording,
 * and where it joined. Search by part of an address. Exporting the list and taking an address off it are
 * Admin-only and audited at the API, because the list is personal data; an Editor sees neither action.
 */
@Component({
  selector: 'cms-newsletter-list',
  imports: [
    Sortable,
    SortHeader,
    SortPicker,
    FormsTabs,
    UiButton,
    UiControl,
    UiEmptyState,
    UiField,
    UiNotice,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './newsletter-list.html',
  styleUrl: './newsletter-list.scss',
})
export class NewsletterList {
  private readonly forms = inject(FormsService);
  private readonly session = inject(SessionService);
  private readonly confirm = inject(ConfirmService);
  private readonly download = inject(BrowserDownload);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  private generation = 0;

  protected readonly limits = FORMS_LIMITS;
  protected readonly sources = NEWSLETTER_SOURCE_LABELS;
  protected readonly isAdmin = computed(() => this.session.hasAnyRole([ROLES.admin]));
  protected readonly search = signal('');
  protected readonly query = signal('');
  protected readonly state = signal<'loading' | 'failed' | 'ready'>('loading');
  protected readonly items = signal<readonly NewsletterSubscriber[]>([]);
  protected readonly next = signal<string | null>(null);
  protected readonly loadingMore = signal(false);
  protected readonly moreFailed = signal(false);
  protected readonly refreshing = signal(false);
  protected readonly exporting = signal(false);
  /** The subscriber being removed, while the request is out. */
  protected readonly removing = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly liveMessage = signal('');

  /** The order, kept in the address bar as ?sort= so Back returns to it. */
  protected readonly sort = new ListSort(SORTS, { column: 'consented', direction: 'desc' });

  constructor() {
    effect(() => {
      this.query();
      this.sort.current();
      untracked(() => void this.reload());
    });
    inject(DestroyRef).onDestroy(() => this.clearSearchTimer());
  }

  protected instant(iso: string): string {
    return formatInstant(iso);
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
      const page = await firstValueFrom(this.forms.listSubscribers(this.currentQuery()));
      if (generation !== this.generation) return;
      this.items.set(page.items);
      this.next.set(page.nextCursor);
      this.state.set('ready');
      this.liveMessage.set(
        this.sort.announcement() + countOf(page.items.length, page.nextCursor !== null),
      );
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
        this.forms.listSubscribers({ ...this.currentQuery(), cursor }),
      );
      if (generation !== this.generation) return;
      const firstNew = this.items().length;
      this.items.update((items) => [...items, ...page.items]);
      this.next.set(page.nextCursor);
      this.liveMessage.set(
        `${page.items.length} more ${page.items.length === 1 ? 'address' : 'addresses'} loaded.`,
      );
      // The rows have no link to land on, so the first new row itself takes focus: "Load more" may be gone.
      afterNextRender(
        () =>
          this.host.nativeElement.querySelector<HTMLElement>(`tr[data-row="${firstNew}"]`)?.focus(),
        { injector: this.injector },
      );
    } catch {
      if (generation === this.generation) this.moreFailed.set(true);
    } finally {
      this.loadingMore.set(false);
    }
  }

  /** Downloads the whole list as CSV (Admins only). A refusal shows here; nothing is kept in the page. */
  protected async export(): Promise<void> {
    if (!this.isAdmin() || this.exporting()) return;

    this.exporting.set(true);
    this.actionError.set(null);
    try {
      const file = await firstValueFrom(this.forms.exportSubscribers());
      this.download.save(file.blob, file.fileName);
      this.announce(`Exported the list as ${file.fileName}.`);
    } catch (error: unknown) {
      this.fail(messageOf(problemOf(error), 'The list couldn’t be exported. Try again.'));
    } finally {
      this.exporting.set(false);
    }
  }

  /** Takes one address off the list (Admins only), after asking. */
  protected async remove(subscriber: NewsletterSubscriber): Promise<void> {
    if (!this.isAdmin() || this.removing() !== null) return;

    const confirmed = await this.confirm.confirm({
      heading: 'Remove this address?',
      message: `${subscriber.email} will get no more newsletters from 6311arts. The removal is recorded under your name.`,
      confirmLabel: 'Remove address',
      cancelLabel: 'Keep it',
    });
    if (!confirmed) return;

    this.removing.set(subscriber.id);
    this.actionError.set(null);
    try {
      await firstValueFrom(this.forms.removeSubscriber(subscriber.id));
      this.drop(subscriber);
      this.announce(`Removed ${subscriber.email} from the list.`);
    } catch (error: unknown) {
      const problem = problemOf(error);
      if (problem?.code === FORMS_CODES.notFound) {
        this.drop(subscriber);
        this.announce(`${subscriber.email} was already off the list.`);
      } else {
        this.fail(messageOf(problem, 'The address couldn’t be removed. Try again.'));
      }
    } finally {
      this.removing.set(null);
    }
  }

  /** Takes the row away and keeps the keyboard where it was: the next row's Remove, or the search box. */
  private drop(subscriber: NewsletterSubscriber): void {
    const index = this.items().findIndex((item) => item.id === subscriber.id);
    this.items.update((items) => items.filter((item) => item.id !== subscriber.id));
    afterNextRender(
      () => {
        const root = this.host.nativeElement;
        const target =
          root.querySelector<HTMLElement>(`[data-remove="${index}"]`) ??
          root.querySelector<HTMLElement>(`[data-remove="${index - 1}"]`) ??
          root.querySelector<HTMLElement>('input[name="search"]');
        target?.focus();
      },
      { injector: this.injector },
    );
  }

  /** The notice is an alert, so it is announced once by itself, not again through the live region. */
  private fail(message: string): void {
    this.actionError.set(message);
  }

  private announce(message: string): void {
    this.liveMessage.update((current) => (current === message ? `${message} ` : message));
  }

  private currentQuery(): NewsletterListQuery {
    const search = this.query();
    return { sort: this.sort.param(), limit: PAGE_SIZE, ...(search ? { search } : {}) };
  }

  private clearSearchTimer(): void {
    if (this.searchTimer !== null) clearTimeout(this.searchTimer);
    this.searchTimer = null;
  }
}

function problemOf(error: unknown): FormsProblem | null {
  return error instanceof HttpErrorResponse ? decodeFormsProblem(error.error, error.status) : null;
}

function messageOf(problem: FormsProblem | null, fallback: string): string {
  if (problem === null || problem.status === 0) return 'Can’t reach 6311arts. Try again.';
  if (problem.status === 403) return 'Only an Admin can do that.';
  const message = problem.detail ?? fallback;
  return problem.status >= 500 && problem.correlationId !== null
    ? `${message} Quote reference ${problem.correlationId} if you report it.`
    : message;
}
