import {
  ChangeDetectionStrategy,
  Component,
  afterNextRender,
  computed,
  inject,
  signal,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiNotice } from '@6311/ui';
import { BrowserDownload } from '../../../core/browser/browser-download';
import { ConfirmService } from '../../../core/confirm/confirm.service';
import { MessageOptions, messageOf, problemOf } from '../../../core/http/problem-message';
import { SessionService } from '../../../core/session/session.service';
import {
  FORMS_CODES,
  FORMS_LIMITS,
  NEWSLETTER_SOURCE_LABELS,
  NewsletterListQuery,
  NewsletterSubscriber,
} from '../../../models/forms';
import { ROLES } from '../../../models/session';
import { FormsService } from '../../../services/forms.service';
import { ListBody } from '../../../shared/list-body/list-body';
import { ListTools } from '../../../shared/list-tools/list-tools';
import { RecordList } from '../../../shared/record-list';
import { SortHeader } from '../../../shared/sort-header/sort-header';
import { Sortable } from '../../../shared/sortable';
import { FormsTabs, formatInstant } from '../forms-tabs/forms-tabs';

/** How a refused export or removal is put: the list holds no edits, and only an Admin may do either. */
const REFUSALS: MessageOptions = { edits: false, forbidden: 'Only an Admin can do that.' };

/**
 * The newsletter list (`/admin/forms/newsletter`), newest first: each address, when it agreed and to which wording,
 * and where it joined. Search by part of an address. Exporting the list and taking an address off it are
 * Admin-only and audited at the API, because the list is personal data; an Editor sees neither action.
 */
@Component({
  selector: 'cms-newsletter-list',
  imports: [FormsTabs, ListBody, ListTools, SortHeader, Sortable, UiButton, UiNotice],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './newsletter-list.html',
  styleUrl: './newsletter-list.scss',
})
export class NewsletterList extends RecordList<NewsletterSubscriber, 'all', NewsletterListQuery> {
  private readonly forms = inject(FormsService);
  private readonly session = inject(SessionService);
  private readonly confirm = inject(ConfirmService);
  private readonly download = inject(BrowserDownload);

  protected readonly limits = FORMS_LIMITS;
  protected readonly sources = NEWSLETTER_SOURCE_LABELS;
  protected readonly instant = formatInstant;
  protected readonly isAdmin = computed(() => this.session.hasAnyRole([ROLES.admin]));
  protected readonly exporting = signal(false);
  /** The subscriber being removed, while the request is out. */
  protected readonly removing = signal<string | null>(null);
  /** An export or removal refused. The notice is an alert, so it is announced once by itself. */
  protected readonly actionError = signal<string | null>(null);

  constructor() {
    const forms = inject(FormsService);
    super({
      list: (query) => forms.listSubscribers(query),
      noun: 'address',
      plural: 'addresses',
      loading: 'Loading the newsletter list…',
      failed: 'The newsletter list couldn’t be loaded',
      icon: 'mail',
      filters: [],
      filter: 'all',
      filterQuery: () => ({}),
      sorts: [
        { key: 'email', label: 'Email', kind: 'text' },
        { key: 'consented', label: 'Consented', kind: 'date' },
        { key: 'wording', label: 'Consent wording', kind: 'text' },
        {
          key: 'source',
          label: 'Source',
          kind: 'status',
          words: { asc: 'sign-ups first', desc: 'submissions first' },
        },
      ],
      sort: { column: 'consented', direction: 'desc' },
    });
  }

  protected emptyHeading(): string {
    return this.query() === '' ? 'No one on the list yet' : 'Nothing to show here';
  }

  protected emptyText(): string {
    return this.query() === ''
      ? 'Addresses from the newsletter sign-up and from artist submissions arrive here.'
      : `No address contains “${this.query()}”.`;
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
      this.actionError.set(
        messageOf(problemOf(error), 'The list couldn’t be exported. Try again.', REFUSALS),
      );
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
        this.actionError.set(
          messageOf(problem, 'The address couldn’t be removed. Try again.', REFUSALS),
        );
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
}
