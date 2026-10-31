import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { UiStatusChip } from '@6311/ui';
import {
  ARTIST_LIMITS,
  ArtistListQuery,
  ArtistSummary,
  STATUS_CHIPS,
} from '../../../models/artist';
import { ArtistService } from '../../../services/artist.service';
import { ContentList } from '../../../shared/content-list';
import { ListBody } from '../../../shared/list-body/list-body';
import { ListHead } from '../../../shared/list-head/list-head';
import { ListTools } from '../../../shared/list-tools/list-tools';
import { updatedOn } from '../../../shared/record-list';
import { SortHeader } from '../../../shared/sort-header/sort-header';
import { Sortable } from '../../../shared/sortable';

/**
 * The roster (`/admin/artists`): every artist by name, with what state each is in. Filter by status, search by
 * name, and open one to edit it.
 */
@Component({
  selector: 'cms-artist-list',
  imports: [ListBody, ListHead, ListTools, RouterLink, SortHeader, Sortable, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './artist-list.html',
  styleUrl: './artist-list.scss',
})
export class ArtistList extends ContentList<ArtistSummary, ArtistListQuery> {
  protected readonly chips = STATUS_CHIPS;
  protected readonly limits = ARTIST_LIMITS;
  protected readonly updated = updatedOn;

  constructor() {
    super({
      api: inject(ArtistService),
      noun: 'artist',
      plural: 'artists',
      icon: 'brush',
      field: 'name',
      firstHint: 'Add the first artist with “New artist”.',
      sorts: [
        { key: 'name', label: 'Name', kind: 'text' },
        { key: 'medium', label: 'Medium', kind: 'text' },
        { key: 'status', label: 'Status', kind: 'status' },
        { key: 'showing', label: 'Showing', kind: 'flag' },
        { key: 'updated', label: 'Updated', kind: 'date' },
      ],
      sort: { column: 'name', direction: 'asc' },
    });
  }
}
