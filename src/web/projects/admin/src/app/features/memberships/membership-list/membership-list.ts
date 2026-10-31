import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { UiStatusChip } from '@6311/ui';
import { STATUS_CHIPS } from '../../../models/content';
import {
  MEMBERSHIP_LIMITS,
  MembershipPlanListQuery,
  MembershipPlanSummary,
  cadenceWords,
} from '../../../models/membership';
import { MembershipService } from '../../../services/membership.service';
import { ContentList } from '../../../shared/content-list';
import { ListBody } from '../../../shared/list-body/list-body';
import { ListHead } from '../../../shared/list-head/list-head';
import { ListTools } from '../../../shared/list-tools/list-tools';
import { formatMoney } from '../../../shared/money';
import { updatedOn } from '../../../shared/record-list';
import { SortHeader } from '../../../shared/sort-header/sort-header';
import { Sortable } from '../../../shared/sortable';

/**
 * The membership tiers (`/admin/memberships`), in the order the site shows them, with their price, perks and class
 * discount. Filter by status, search by name, and open one to edit it.
 */
@Component({
  selector: 'cms-membership-list',
  imports: [ListBody, ListHead, ListTools, RouterLink, SortHeader, Sortable, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './membership-list.html',
  styleUrl: './membership-list.scss',
})
export class MembershipList extends ContentList<MembershipPlanSummary, MembershipPlanListQuery> {
  protected readonly chips = STATUS_CHIPS;
  protected readonly limits = MEMBERSHIP_LIMITS;
  protected readonly updated = updatedOn;

  constructor() {
    super({
      api: inject(MembershipService),
      noun: 'tier',
      plural: 'tiers',
      collection: 'membership tiers',
      deletedNoun: { one: 'plan', many: 'plans' },
      icon: 'info',
      field: 'name',
      firstHint: 'Add the first tier with “New tier”.',
      sorts: [
        { key: 'tier', label: 'Tier', kind: 'text' },
        { key: 'status', label: 'Status', kind: 'status' },
        { key: 'price', label: 'Price', kind: 'number' },
        { key: 'discount', label: 'Class discount', kind: 'number' },
        { key: 'updated', label: 'Updated', kind: 'date' },
      ],
      sort: { column: 'order', direction: 'asc' },
      sortWords: 'the order the site shows them in',
    });
  }

  protected perkCount(plan: MembershipPlanSummary): string {
    return plan.perkCount === 1 ? '1 perk' : `${plan.perkCount} perks`;
  }

  /** What the tier bills: Square's figure once it has reported one, the CMS's draft price until then. */
  protected price(plan: MembershipPlanSummary): string {
    const cents = plan.priceCents ?? plan.startingPriceCents;
    return cents === null ? '—' : formatMoney(cents, plan.currency);
  }

  protected priceSource(plan: MembershipPlanSummary): string {
    if (plan.priceCents === null) return 'Not in Square yet';
    return plan.cadence === null ? 'In Square' : `In Square · ${cadenceWords(plan.cadence)}`;
  }
}
