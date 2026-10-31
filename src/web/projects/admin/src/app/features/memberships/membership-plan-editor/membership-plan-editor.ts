import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiControl, UiField } from '@6311/ui';
import { isSlug, slugFromName } from '../../../models/content';
import {
  MEMBERSHIP_CADENCES,
  MEMBERSHIP_LIMITS,
  MembershipCadence,
  MembershipPlan,
  MembershipPlanCreate,
  MembershipPlanUpdate,
  cadenceWords,
} from '../../../models/membership';
import { MembershipService } from '../../../services/membership.service';
import { ContentEditor } from '../../../shared/content-editor';
import { EditorFrame } from '../../../shared/editor-frame/editor-frame';
import { centsToPrice, formatMoney, priceToCents, squarePrice, typed } from '../../../shared/money';
import { PublishingPanel } from '../../../shared/publishing-panel/publishing-panel';
import { CompareRow, FieldErrors, orDash, yesNo } from '../../../shared/record-editor';
import { SaveActions } from '../../../shared/save-actions/save-actions';

interface Draft {
  readonly name: string;
  readonly slug: string;
  /** Whether the person has typed a slug; until then it follows the name (new tiers only). */
  readonly slugEdited: boolean;
  /** The perks, in the order the page will list them. */
  readonly perks: readonly string[];
  /** Whole percent, as typed. */
  readonly discount: string;
  readonly featured: boolean;
  /** Where the tier comes, as typed. */
  readonly order: string;
  /** Dollars, as typed; the API takes cents. */
  readonly price: string;
  readonly cadence: MembershipCadence;
}

const FIELDS = [
  'name',
  'slug',
  'perks',
  'discountPercent',
  'displayOrder',
  'startingPriceCents',
  'startingCadence',
] as const;
type Field = (typeof FIELDS)[number];

/** Which field's error a draft key clears, where the two names differ. */
const DRAFT_FIELDS: Readonly<Record<string, Field>> = {
  discount: 'discountPercent',
  order: 'displayOrder',
  price: 'startingPriceCents',
  cadence: 'startingCadence',
};

const EMPTY: Draft = {
  name: '',
  slug: '',
  slugEdited: false,
  perks: [''],
  discount: '',
  featured: false,
  order: '',
  price: '',
  cadence: 'Annual',
};

/**
 * One membership tier (`/admin/memberships/new`, `/admin/memberships/:id`): its name, key, perks, class discount
 * and where it comes on the page; then publish, unpublish or (Admins) archive.
 *
 * The price and the billing cadence are ordinary fields only until Square holds the plan; after that Square's own
 * figures are shown read-only, because they are Square's record (square.md, ADR 0005). A tier has no page of its
 * own: every published tier is shown on `/memberships`, so its key is never a web address (ADR 0040).
 */
@Component({
  selector: 'cms-membership-plan-editor',
  imports: [FormsModule, UiControl, UiField, EditorFrame, PublishingPanel, SaveActions],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './membership-plan-editor.html',
  styleUrl: './membership-plan-editor.scss',
})
export class MembershipPlanEditor extends ContentEditor<
  MembershipPlan,
  Draft,
  MembershipPlanCreate,
  MembershipPlanUpdate,
  Field
> {
  protected readonly limits = MEMBERSHIP_LIMITS;
  protected readonly cadences = MEMBERSHIP_CADENCES;
  protected readonly cleanPerks = cleanPerks;
  protected readonly typed = typed;
  protected readonly plan = this.record;

  /** ADR 0005: the price and the cadence are ordinary fields only until Square holds the plan. */
  protected readonly priceEditable = computed(() => this.plan()?.startingPriceEditable ?? true);

  constructor() {
    super({
      api: inject(MembershipService),
      noun: 'tier',
      plural: 'tiers',
      route: '/memberships',
      label: (plan) => plan.name,
      createdMessage: 'Tier created. Publish it when it’s ready.',
      createdState: 'membershipPlanCreated',
      empty: () => EMPTY,
      draftOf,
      createOf,
      changesOf,
      started: (draft) =>
        draft.name.trim() !== '' ||
        cleanPerks(draft.perks).length > 0 ||
        draft.discount !== '' ||
        draft.order !== '' ||
        draft.price !== '' ||
        draft.featured,
      localErrors,
      fields: FIELDS,
      lifecycle: {
        noun: 'tier',
        deleteNoun: 'plan',
        keepLabel: 'Keep it',
        inSquare: true,
        previewType: 'membership-plan',
        unpublishMessage:
          'It goes back to being a draft and leaves the memberships page. Members who already joined are unaffected: Square owns their subscriptions.',
        archiveMessage:
          'An archived tier is retired from the site, and archiving can’t be undone from Studio. The archive is recorded under your name.',
        deleteText:
          'The plan leaves the site and the list, and its web address is free again. An Admin can restore it from Deleted, as a draft.',
        published: 'Published: on the memberships page. Edits show there within a minute.',
        redrafted: 'A draft again, after being published.',
        draft: 'A draft: only staff can see it. Publishing needs a price and at least one perk.',
      },
    });
  }

  /** A new tier's key follows its name until the person types one. */
  protected setName(name: string): void {
    const followsName = !this.plan() && !this.draft().slugEdited;
    this.patch({ name, ...(followsName ? { slug: slugFromName(name) } : {}) });
  }

  protected setPerk(index: number, text: string): void {
    this.patch({ perks: this.draft().perks.map((perk, at) => (at === index ? text : perk)) });
  }

  protected addPerk(): void {
    if (this.draft().perks.length >= MEMBERSHIP_LIMITS.perksMaxCount) return;
    this.patch({ perks: [...this.draft().perks, ''] });
  }

  protected removePerk(index: number): void {
    const perks = this.draft().perks.filter((_, at) => at !== index);
    this.patch({ perks: perks.length === 0 ? [''] : perks });
  }

  protected movePerk(index: number, by: -1 | 1): void {
    const perks = [...this.draft().perks];
    const to = index + by;
    if (to < 0 || to >= perks.length) return;
    [perks[index], perks[to]] = [perks[to], perks[index]];
    this.patch({ perks });
  }

  protected squarePrice(plan: MembershipPlan): string {
    return squarePrice(plan.priceCents, plan.currency);
  }

  protected squareCadence(plan: MembershipPlan): string {
    return plan.cadence === null
      ? 'Square has not reported how often it bills yet.'
      : `Bills in Square: ${cadenceWords(plan.cadence)}`;
  }

  protected override compare(theirs: MembershipPlan, draft: Draft): readonly CompareRow[] {
    return [
      { label: 'Name', mine: orDash(draft.name), theirs: theirs.name },
      {
        label: 'Perks',
        mine: perkSummary(cleanPerks(draft.perks)),
        theirs: perkSummary(theirs.perks),
      },
      {
        label: 'Class discount',
        mine: `${draft.discount || '0'}%`,
        theirs: `${theirs.discountPercent}%`,
      },
      { label: 'Order', mine: orDash(draft.order), theirs: String(theirs.displayOrder) },
      { label: 'Key', mine: orDash(draft.slug), theirs: theirs.slug },
      { label: 'Featured', mine: yesNo(draft.featured), theirs: yesNo(theirs.featured) },
      ...(theirs.startingPriceEditable
        ? [
            {
              label: 'Starting price',
              mine: draft.price ? `$${draft.price}` : '—',
              theirs:
                theirs.startingPriceCents === null
                  ? '—'
                  : formatMoney(theirs.startingPriceCents, theirs.currency),
            },
            { label: 'Billing cadence', mine: draft.cadence, theirs: theirs.startingCadence },
          ]
        : []),
    ];
  }

  protected override fieldsOf(keys: readonly string[]): readonly string[] {
    return keys.map((key) => DRAFT_FIELDS[key] ?? key);
  }
}

function perkSummary(perks: readonly string[]): string {
  return perks.length === 0 ? '—' : perks.join('; ');
}

function wholeNumber(value: string): number | null {
  const trimmed = value.trim();
  if (trimmed === '') return null;
  return /^\d+$/.test(trimmed) ? Number(trimmed) : null;
}

/** The perks as they would be saved: trimmed, with the empty rows the form keeps for typing dropped. */
function cleanPerks(perks: readonly string[]): readonly string[] {
  return perks.map((perk) => perk.trim()).filter((perk) => perk !== '');
}

function draftOf(plan: MembershipPlan): Draft {
  return {
    name: plan.name,
    slug: plan.slug,
    slugEdited: true,
    perks: plan.perks.length === 0 ? [''] : [...plan.perks],
    discount: String(plan.discountPercent),
    featured: plan.featured,
    order: String(plan.displayOrder),
    price: plan.startingPriceCents === null ? '' : centsToPrice(plan.startingPriceCents),
    cadence: plan.startingCadence,
  };
}

function createOf(draft: Draft): MembershipPlanCreate {
  const perks = cleanPerks(draft.perks);
  const cents = priceToCents(draft.price);
  const discount = wholeNumber(draft.discount);
  const order = wholeNumber(draft.order);
  return {
    name: draft.name.trim(),
    ...(draft.slug ? { slug: draft.slug } : {}),
    ...(perks.length > 0 ? { perks } : {}),
    ...(discount === null ? {} : { discountPercent: discount }),
    ...(draft.featured ? { featured: true } : {}),
    ...(order === null ? {} : { displayOrder: order }),
    ...(cents === null ? {} : { startingPriceCents: cents }),
    startingCadence: draft.cadence,
  };
}

/**
 * Only what changed. The price and the cadence are left out once Square holds the plan, whatever the form shows,
 * because the API refuses them then (ADR 0005); an emptied price is sent as `removeStartingPrice`.
 */
function changesOf(plan: MembershipPlan, draft: Draft): MembershipPlanUpdate {
  const name = draft.name.trim();
  const perks = cleanPerks(draft.perks);
  const discount = wholeNumber(draft.discount);
  const order = wholeNumber(draft.order);
  const cents = priceToCents(draft.price);
  const samePerks =
    perks.length === plan.perks.length && perks.every((perk, index) => perk === plan.perks[index]);

  return {
    ...(name !== plan.name ? { name } : {}),
    ...(draft.slug !== plan.slug ? { slug: draft.slug } : {}),
    ...(samePerks ? {} : { perks }),
    ...(discount !== null && discount !== plan.discountPercent
      ? { discountPercent: discount }
      : {}),
    ...(draft.featured !== plan.featured ? { featured: draft.featured } : {}),
    ...(order !== null && order !== plan.displayOrder ? { displayOrder: order } : {}),
    ...(plan.startingPriceEditable && cents !== plan.startingPriceCents
      ? cents === null
        ? { removeStartingPrice: true }
        : { startingPriceCents: cents }
      : {}),
    ...(plan.startingPriceEditable && draft.cadence !== plan.startingCadence
      ? { startingCadence: draft.cadence }
      : {}),
  };
}

function localErrors(draft: Draft): FieldErrors<Field> {
  const errors: FieldErrors<Field> = {};
  if (draft.name.trim() === '') errors.name = 'Enter the tier’s name.';
  if (!isSlug(draft.slug)) {
    errors.slug = draft.slug
      ? 'Use lower-case letters, digits and single hyphens (for example gray-membership).'
      : 'Enter a key, for example gray-membership.';
  }

  const perks = cleanPerks(draft.perks);
  if (new Set(perks.map((perk) => perk.toLowerCase())).size !== perks.length) {
    errors.perks = 'Each perk is listed once.';
  }

  if (draft.discount.trim() !== '' && wholeNumber(draft.discount) === null) {
    errors.discountPercent = 'Enter the discount as whole percent, for example 10.';
  }
  if (draft.order.trim() !== '' && wholeNumber(draft.order) === null) {
    errors.displayOrder = 'Enter where the tier comes as a whole number, for example 2.';
  }
  if (draft.price.trim() !== '' && priceToCents(draft.price) === null) {
    errors.startingPriceCents = 'Enter the price as a number, for example 100 or 99.50.';
  }

  return errors;
}
