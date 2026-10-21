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
import { BrowserDownload } from '../../core/browser/browser-download';
import { ConfirmService } from '../../core/confirm/confirm.service';
import { SessionService } from '../../core/session/session.service';
import {
  FORMS_CODES,
  FORMS_LIMITS,
  FormsProblem,
  NEWSLETTER_SOURCE_LABELS,
  NewsletterListQuery,
  NewsletterSubscriber,
  decodeFormsProblem,
} from '../../models/forms';
import { ROLES } from '../../models/session';
import { FormsService } from '../../services/forms.service';
import { FormsTabs, formatInstant } from './forms-tabs';

/** How long typing pauses before the search is sent. */
const SEARCH_DELAY_MS = 300;

const PAGE_SIZE = 50;

function countOf(count: number, more: boolean): string {
  if (count === 0) return 'No addresses to show.';
  return `${more ? 'Showing the first ' : ''}${count} ${count === 1 ? 'address' : 'addresses'}.`;
}

/**
 * The newsletter list (`/admin/forms/newsletter`), newest first: each address, when it agreed and to which wording,
 * and where it joined. Search by part of an address. Exporting the list and taking an address off it are
 * Admin-only and audited at the API, because the list is personal data; an Editor sees neither action.
 */
@Component({
  selector: 'cms-newsletter-list',
  imports: [FormsTabs, UiButton, UiControl, UiEmptyState, UiField, UiNotice],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <cms-forms-tabs />

    <div class="tools">
      <ui-field label="Find an address" class="tools__search">
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
      @if (isAdmin()) {
        <button uiButton type="button" variant="dark" [busy]="exporting()" (click)="export()">
          Export CSV
        </button>
      } @else {
        <p class="hint">Only an Admin can export the list or take an address off it.</p>
      }
    </div>

    <p class="sr-only" aria-live="polite">{{ liveMessage() }}</p>

    @if (actionError(); as message) {
      <ui-notice tone="error">{{ message }}</ui-notice>
    }

    @switch (state()) {
      @case ('loading') {
        <p role="status">Loading the newsletter list…</p>
      }
      @case ('failed') {
        <ui-notice tone="error" heading="The newsletter list couldn’t be loaded">
          <p>6311arts may be unreachable for a moment.</p>
          <button uiButton type="button" size="sm" (click)="reload()">Try again</button>
        </ui-notice>
      }
      @default {
        @if (items().length === 0) {
          <ui-empty-state
            icon="mail"
            [heading]="query() === '' ? 'No one on the list yet' : 'Nothing to show here'"
          >
            <p>
              {{
                query() === ''
                  ? 'Addresses from the newsletter sign-up and from artist submissions arrive here.'
                  : 'No address contains “' + query() + '”.'
              }}
            </p>
          </ui-empty-state>
        } @else {
          <div class="scroll" role="region" aria-label="Newsletter list" tabindex="0">
            <table class="list" [attr.aria-busy]="refreshing() || null">
              <caption class="sr-only">
                Newsletter addresses, newest first
              </caption>
              <thead>
                <tr>
                  <th scope="col">Email</th>
                  <th scope="col" class="wide">Consented</th>
                  <th scope="col" class="wide">Consent wording</th>
                  <th scope="col">Source</th>
                  @if (isAdmin()) {
                    <th scope="col"><span class="sr-only">Actions</span></th>
                  }
                </tr>
              </thead>
              <tbody>
                @for (subscriber of items(); track subscriber.id; let index = $index) {
                  <tr tabindex="-1" [attr.data-row]="index">
                    <th scope="row" class="text">{{ subscriber.email }}</th>
                    <td class="wide">
                      <time [attr.datetime]="subscriber.consentedAt">{{
                        instant(subscriber.consentedAt)
                      }}</time>
                    </td>
                    <td class="wide">{{ subscriber.consentVersion }}</td>
                    <td>{{ sources[subscriber.source] }}</td>
                    @if (isAdmin()) {
                      <td class="action">
                        <button
                          uiButton
                          type="button"
                          size="sm"
                          [attr.data-remove]="index"
                          [attr.aria-label]="'Remove ' + subscriber.email"
                          [busy]="removing() === subscriber.id"
                          [disabled]="removing() !== null"
                          (click)="remove(subscriber)"
                        >
                          Remove
                        </button>
                      </td>
                    }
                  </tr>
                }
              </tbody>
            </table>
          </div>
          @if (moreFailed()) {
            <ui-notice tone="error" heading="More addresses couldn’t be loaded">
              <p>The addresses above are still here.</p>
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
    .tools {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-4);
      align-items: flex-end;
      justify-content: space-between;
    }
    .tools__search {
      flex: 1 1 260px;
      max-width: 420px;
    }
    .hint {
      margin: 0;
      color: var(--muted);
      font-size: var(--fs-small);
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
    .text {
      overflow-wrap: anywhere;
    }
    .action {
      text-align: right;
    }
    .more {
      text-align: center;
    }
    @media (max-width: 600px) {
      .wide {
        display: none;
      }
    }
  `,
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

  constructor() {
    effect(() => {
      this.query();
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
    return { limit: PAGE_SIZE, ...(search ? { search } : {}) };
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
