import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { UiStatusChip } from '@6311/ui';
import { ContactMessage, ContactMessageListQuery, HANDLED_CHIPS } from '../../../models/forms';
import { FormsService } from '../../../services/forms.service';
import { ListBody } from '../../../shared/list-body/list-body';
import { ListTools } from '../../../shared/list-tools/list-tools';
import { RecordList } from '../../../shared/record-list';
import { SortHeader } from '../../../shared/sort-header/sort-header';
import { Sortable } from '../../../shared/sortable';
import { FormsTabs, formatInstant } from '../forms-tabs/forms-tabs';

type Filter = 'all' | 'unhandled' | 'handled';

/** How much of a message the list shows before "…". */
const EXCERPT_LENGTH = 120;

/**
 * Messages from the site's contact form (`/admin/forms/messages`), newest first, with whether each has been dealt
 * with. Filter by handled, and open one to read it in full and mark it.
 */
@Component({
  selector: 'cms-message-list',
  imports: [FormsTabs, ListBody, ListTools, RouterLink, SortHeader, Sortable, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './message-list.html',
  styleUrl: './message-list.scss',
})
export class MessageList extends RecordList<ContactMessage, Filter, ContactMessageListQuery> {
  protected readonly received = formatInstant;

  constructor() {
    const forms = inject(FormsService);
    super({
      list: (query) => forms.listMessages(query),
      noun: 'message',
      plural: 'messages',
      icon: 'chat',
      filters: [
        { value: 'all', label: 'All' },
        { value: 'unhandled', label: 'Unhandled' },
        { value: 'handled', label: 'Handled' },
      ],
      filter: 'all',
      filterQuery: (filter) => (filter === 'all' ? {} : { handled: filter === 'handled' }),
      sorts: [
        { key: 'from', label: 'From', kind: 'text' },
        { key: 'message', label: 'Message', kind: 'text' },
        {
          key: 'status',
          label: 'Status',
          kind: 'flag',
          words: { asc: 'new first', desc: 'handled first' },
        },
        { key: 'received', label: 'Received', kind: 'date' },
      ],
      sort: { column: 'received', direction: 'desc' },
    });
  }

  protected emptyHeading(): string {
    return this.filter() === 'all' ? 'No messages yet' : 'Nothing to show here';
  }

  protected emptyText(): string {
    switch (this.filter()) {
      case 'all':
        return 'Messages from the site’s contact form arrive here.';
      case 'unhandled':
        return 'Every message has been handled.';
      default:
        return 'No message has been marked handled yet.';
    }
  }

  protected chipOf(message: ContactMessage) {
    return HANDLED_CHIPS[message.handled ? 'handled' : 'unhandled'];
  }

  /** The start of a message on one line. Bound as text: a visitor wrote it. */
  protected excerpt(message: string): string {
    const flat = message.replace(/\s+/g, ' ').trim();
    return flat.length > EXCERPT_LENGTH ? `${flat.slice(0, EXCERPT_LENGTH).trimEnd()}…` : flat;
  }
}
