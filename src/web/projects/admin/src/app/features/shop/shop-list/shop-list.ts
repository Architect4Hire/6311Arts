import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { UiButton, UiControl, UiField, UiStatusChip } from '@6311/ui';
import { STATUS_CHIPS } from '../../../models/content';
import {
  SHOP_CATEGORIES,
  SHOP_LIMITS,
  ShopItemSummary,
  ShopListQuery,
  categoryLabel,
} from '../../../models/shop';
import { ShopService } from '../../../services/shop.service';
import { ContentList } from '../../../shared/content-list';
import { ListBody } from '../../../shared/list-body/list-body';
import { ListHead } from '../../../shared/list-head/list-head';
import { ListTools } from '../../../shared/list-tools/list-tools';
import { formatMoney } from '../../../shared/money';
import { updatedOn } from '../../../shared/record-list';
import { SortHeader } from '../../../shared/sort-header/sort-header';
import { Sortable } from '../../../shared/sortable';

/**
 * The artwork the shop sells (`/admin/shop`), by title, with its artist, its state and what Square says about it.
 * Filter by status or category, search by title, and open a piece to edit it.
 */
@Component({
  selector: 'cms-shop-list',
  imports: [
    ListBody,
    ListHead,
    ListTools,
    RouterLink,
    SortHeader,
    Sortable,
    UiButton,
    UiControl,
    UiField,
    UiStatusChip,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shop-list.html',
  styleUrl: './shop-list.scss',
})
export class ShopList extends ContentList<ShopItemSummary, ShopListQuery> {
  protected readonly chips = STATUS_CHIPS;
  protected readonly limits = SHOP_LIMITS;
  protected readonly categories = SHOP_CATEGORIES;
  protected readonly category = signal('');
  protected readonly updated = updatedOn;

  constructor() {
    super({
      api: inject(ShopService),
      noun: 'piece',
      plural: 'pieces',
      collection: 'artwork',
      failed: 'The shop couldn’t be loaded',
      icon: 'cart',
      field: 'title',
      firstHint: 'Add the first piece with “New piece”.',
      scheduled: true,
      sorts: [
        { key: 'title', label: 'Title', kind: 'text' },
        { key: 'artist', label: 'Artist', kind: 'text' },
        { key: 'status', label: 'Status', kind: 'status' },
        {
          key: 'square',
          label: 'In Square',
          kind: 'number',
          words: { asc: 'lowest price first', desc: 'highest price first' },
        },
        { key: 'updated', label: 'Updated', kind: 'date' },
      ],
      sort: { column: 'title', direction: 'asc' },
    });
  }

  protected override extraQuery(): Partial<ShopListQuery> {
    const category = this.category();
    return category ? { category } : {};
  }

  protected override extraFilter(): string | null {
    const category = this.category();
    return category ? `Nothing is filed under ${categoryLabel(category)}.` : null;
  }

  /** The line under the title: its SKU, its categories, and whether there is only one of it. */
  protected meta(piece: ShopItemSummary): string {
    return [
      piece.sku,
      piece.categories.map(categoryLabel).join(', '),
      piece.oneOfAKind ? 'One of a kind' : null,
    ]
      .filter(Boolean)
      .join(' · ');
  }

  /** What Square says about the piece, in the words staff scan for. */
  protected square(piece: ShopItemSummary): string {
    const price = piece.priceCents === null ? null : formatMoney(piece.priceCents, piece.currency);
    const stock = piece.stockOnHand === null ? null : `${piece.stockOnHand} in stock`;
    return [price, stock].filter(Boolean).join(' · ');
  }
}
