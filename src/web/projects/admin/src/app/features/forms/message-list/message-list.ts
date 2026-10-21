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
import { ContactMessage, ContactMessageListQuery, HANDLED_CHIPS } from '../../../models/forms';
import { FormsService } from '../../../services/forms.service';
import { FormsTabs, formatInstant } from '../forms-tabs/forms-tabs';

type Filter = 'all' | 'unhandled' | 'handled';

const FILTERS: readonly { readonly value: Filter; readonly label: string }[] = [
  { value: 'all', label: 'All' },
  { value: 'unhandled', label: 'Unhandled' },
  { value: 'handled', label: 'Handled' },
];

const PAGE_SIZE = 50;

/** How much of a message the list shows before "…". */
const EXCERPT_LENGTH = 120;

function countOf(count: number, more: boolean): string {
  if (count === 0) return 'No messages to show.';
  return `${more ? 'Showing the first ' : ''}${count} ${count === 1 ? 'message' : 'messages'}.`;
}

/**
 * Messages from the site's contact form (`/admin/forms/messages`), newest first, with whether each has been dealt
 * with. Filter by handled, and open one to read it in full and mark it.
 */
@Component({
  selector: 'cms-message-list',
  imports: [FormsTabs, RouterLink, UiButton, UiEmptyState, UiNotice, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './message-list.html',
  styleUrl: './message-list.scss',
})
export class MessageList {
  private readonly forms = inject(FormsService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private generation = 0;

  protected readonly filters = FILTERS;
  protected readonly filter = signal<Filter>('all');
  protected readonly state = signal<'loading' | 'failed' | 'ready'>('loading');
  protected readonly items = signal<readonly ContactMessage[]>([]);
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

  protected chipOf(message: ContactMessage) {
    return HANDLED_CHIPS[message.handled ? 'handled' : 'unhandled'];
  }

  /** The start of a message on one line. Bound as text: a visitor wrote it. */
  protected excerpt(message: string): string {
    const flat = message.replace(/\s+/g, ' ').trim();
    return flat.length > EXCERPT_LENGTH ? `${flat.slice(0, EXCERPT_LENGTH).trimEnd()}…` : flat;
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
      const page = await firstValueFrom(this.forms.listMessages(this.currentQuery()));
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
        this.forms.listMessages({ ...this.currentQuery(), cursor }),
      );
      if (generation !== this.generation) return;
      const firstNew = this.items().length;
      this.items.update((items) => [...items, ...page.items]);
      this.next.set(page.nextCursor);
      this.liveMessage.set(
        `${page.items.length} more ${page.items.length === 1 ? 'message' : 'messages'} loaded.`,
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

  private currentQuery(): ContactMessageListQuery {
    const filter = this.filter();
    return {
      limit: PAGE_SIZE,
      ...(filter === 'all' ? {} : { handled: filter === 'handled' }),
    };
  }
}
