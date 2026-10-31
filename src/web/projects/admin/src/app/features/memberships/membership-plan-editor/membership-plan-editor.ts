import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiControl, UiField, UiNotice, UiStatusChip } from '@6311/ui';
import { HasUnsavedChanges, warnBeforeUnload } from '../../../core/guards/unsaved-changes.guard';
import { messageOf, problemOf } from '../../../core/http/problem-message';
import { ContentLifecycle, LifecycleCopy } from '../../../shared/content-lifecycle';
import { PublishingPanel } from '../../../shared/publishing-panel/publishing-panel';
import { STATUS_CHIPS, isSlug, slugFromName } from '../../../models/content';
import {
  MEMBERSHIP_CADENCES,
  MEMBERSHIP_CODES,
  MEMBERSHIP_LIMITS,
  MembershipCadence,
  MembershipPlan,
  MembershipPlanCreate,
  MembershipPlanUpdate,
  MembershipProblem,
} from '../../../models/membership';
import { MembershipService } from '../../../services/membership.service';
import { cadenceWords } from '../membership-list/membership-list';

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

type Status =
  | { readonly kind: 'loading' }
  | { readonly kind: 'load-failed' }
  | { readonly kind: 'not-found' }
  | { readonly kind: 'ready' }
  | { readonly kind: 'saving' }
  | { readonly kind: 'conflict'; readonly theirs: MembershipPlan };

type Field =
  | 'name'
  | 'slug'
  | 'perks'
  | 'discountPercent'
  | 'displayOrder'
  | 'startingPriceCents'
  | 'startingCadence';

type FieldErrors = Partial<Record<Field, string>>;

const FIELDS: readonly Field[] = [
  'name',
  'slug',
  'perks',
  'discountPercent',
  'displayOrder',
  'startingPriceCents',
  'startingCadence',
];

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

/** Router state set by "Create tier", read by the editor it navigates to. */
const CREATED_STATE = 'membershipPlanCreated';

const LIFECYCLE: LifecycleCopy = {
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
  imports: [
    FormsModule,
    RouterLink,
    UiButton,
    UiControl,
    UiField,
    UiNotice,
    UiStatusChip,
    PublishingPanel,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './membership-plan-editor.html',
  styleUrl: './membership-plan-editor.scss',
})
export class MembershipPlanEditor implements HasUnsavedChanges {
  /** The route's `:id`; absent on `/memberships/new`. */
  readonly id = input<string>();

  private readonly memberships = inject(MembershipService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly conflictBox = viewChild<ElementRef<HTMLElement>>('conflictBox');
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  private readonly justCreated =
    this.router.currentNavigation()?.extras.state?.[CREATED_STATE] === true;

  protected readonly chips = STATUS_CHIPS;
  protected readonly limits = MEMBERSHIP_LIMITS;
  protected readonly cadences = MEMBERSHIP_CADENCES;

  protected readonly status = signal<Status>({ kind: 'loading' });
  /** The tier as saved; `null` while creating one. */
  protected readonly plan = signal<MembershipPlan | null>(null);
  protected readonly draft = signal<Draft>(EMPTY);
  protected readonly errors = signal<FieldErrors>({});
  protected readonly formError = signal<string | null>(null);
  protected readonly liveMessage = signal('');

  /** Publish, unpublish, schedule, archive and delete, shown by `<cms-publishing-panel>`. */
  protected readonly lifecycle: ContentLifecycle<MembershipPlan> = new ContentLifecycle({
    copy: LIFECYCLE,
    api: this.memberships,
    record: () => this.plan(),
    label: (saved) => saved.name,
    dirty: () => this.dirty(),
    busy: () => this.busy(),
    replace: (saved) => {
      this.plan.set(saved);
      this.draft.set(draftOf(saved));
    },
    clearErrors: () => {
      this.errors.set({});
      this.formError.set(null);
    },
    refused: (message, problem) => this.showErrors(fieldErrorsOf(problem), message),
    announce: (message) => this.announce(message),
    deleted: (saved) => {
      // Back to the saved version first, so the unsaved-changes guard has nothing to ask about on the way out.
      this.draft.set(draftOf(saved));
      return this.router.navigate(['/memberships']);
    },
  });

  protected readonly busy = computed(() => {
    const kind = this.status().kind;
    return kind === 'saving' || this.lifecycle.busy();
  });
  protected readonly conflict = computed(() => {
    const status = this.status();
    return status.kind === 'conflict' ? status.theirs : null;
  });

  /** ADR 0005: the price and the cadence are ordinary fields only until Square holds the plan. */
  protected readonly priceEditable = computed(() => this.plan()?.startingPriceEditable ?? true);

  protected readonly saveLabel = computed(() => {
    if (this.status().kind === 'saving') return this.plan() ? 'Saving…' : 'Creating…';
    return this.plan() ? 'Save' : 'Create tier';
  });

  /** Edits not yet saved: the unsaved-changes guard asks before they are lost. */
  readonly dirty = computed(() => {
    const draft = this.draft();
    const plan = this.plan();
    if (!plan) {
      return (
        draft.name.trim() !== '' ||
        cleanPerks(draft.perks).length > 0 ||
        draft.discount !== '' ||
        draft.order !== '' ||
        draft.price !== '' ||
        draft.featured
      );
    }
    return Object.keys(changesOf(plan, draft)).length > 0;
  });

  constructor() {
    warnBeforeUnload(() => this.hasUnsavedChanges());

    effect(() => {
      this.id();
      untracked(() => void this.load());
    });
  }

  hasUnsavedChanges(): boolean {
    return this.dirty() || this.busy();
  }

  async load(): Promise<void> {
    const id = this.id();
    this.errors.set({});
    this.formError.set(null);

    if (!id) {
      this.plan.set(null);
      this.draft.set(EMPTY);
      this.status.set({ kind: 'ready' });
      return;
    }

    this.status.set({ kind: 'loading' });
    try {
      const plan = await firstValueFrom(this.memberships.get(id));
      this.plan.set(plan);
      this.draft.set(draftOf(plan));
      this.status.set({ kind: 'ready' });
      if (this.justCreated) {
        this.announce('Tier created. Publish it when it’s ready.');
        this.focus(this.heading);
      }
    } catch (error: unknown) {
      this.status.set(
        problemOf(error)?.code === MEMBERSHIP_CODES.notFound
          ? { kind: 'not-found' }
          : { kind: 'load-failed' },
      );
    }
  }

  protected patch(change: Partial<Draft>): void {
    this.draft.update((draft) => ({ ...draft, ...change }));
    const cleared = Object.keys(change)
      .map((key) => DRAFT_FIELDS[key as keyof Draft])
      .filter((field): field is Field => field !== undefined && field in this.errors());
    if (cleared.length > 0) {
      this.errors.update((errors) =>
        Object.fromEntries(
          Object.entries(errors).filter(([key]) => !cleared.includes(key as Field)),
        ),
      );
    }
  }

  protected setName(name: string): void {
    const followsName = !this.plan() && !this.draft().slugEdited;
    this.patch({ name, ...(followsName ? { slug: slugFromName(name) } : {}) });
  }

  protected setSlug(slug: string): void {
    this.patch({ slug, slugEdited: true });
  }

  /** A number input's model is a number, or null when it is emptied; the draft keeps it as typed. */
  protected setNumber(field: 'discount' | 'order' | 'price', value: string | number | null): void {
    this.patch({ [field]: value === null || value === undefined ? '' : String(value) });
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

  protected cleanPerks(perks: readonly string[]): readonly string[] {
    return cleanPerks(perks);
  }

  protected perkSummary(perks: readonly string[]): string {
    return perks.length === 0 ? '—' : perks.join('; ');
  }

  protected discard(): void {
    const plan = this.plan();
    if (plan) this.draft.set(draftOf(plan));
    this.errors.set({});
    this.formError.set(null);
  }

  protected squarePrice(plan: MembershipPlan): string {
    return plan.priceCents === null
      ? 'Square has not reported a price yet.'
      : `Price in Square: ${money(plan.priceCents, plan.currency)}`;
  }

  protected squareCadence(plan: MembershipPlan): string {
    return plan.cadence === null
      ? 'Square has not reported how often it bills yet.'
      : `Bills in Square: ${cadenceWords(plan.cadence)}`;
  }

  protected async save(etag?: string): Promise<void> {
    if (this.busy() || !this.dirty()) return;

    const draft = this.draft();
    const local = localErrors(draft);
    if (Object.keys(local).length > 0) {
      this.showErrors(local, null);
      return;
    }

    const plan = this.plan();
    this.errors.set({});
    this.formError.set(null);
    this.status.set({ kind: 'saving' });
    try {
      if (!plan) {
        const created = await firstValueFrom(this.memberships.create(createOf(draft)));
        this.plan.set(created);
        this.draft.set(draftOf(created));
        this.status.set({ kind: 'ready' });
        await this.router.navigate(['/memberships', created.id], {
          replaceUrl: true,
          state: { [CREATED_STATE]: true },
        });
        return;
      }

      const saved = await firstValueFrom(
        this.memberships.update(plan.id, changesOf(plan, draft), etag ?? plan.etag),
      );
      this.plan.set(saved);
      this.draft.set(draftOf(saved));
      this.status.set({ kind: 'ready' });
      // Saving disabled the fieldset that held the focused control, so the browser dropped focus to <body>.
      // Put it on the heading, which is where "Saved." is announced from, rather than leaving a keyboard or
      // screen-reader user with no position on the page (WCAG 2.4.3 focus order).
      this.focus(this.heading);
      this.announce('Saved.');
    } catch (error: unknown) {
      await this.saveFailed(error);
    }
  }

  protected async keepMine(theirs: MembershipPlan): Promise<void> {
    this.status.set({ kind: 'ready' });
    this.focus(this.heading);

    // Deliberately *not* `plan.set(theirs)` before saving. `save` diffs the draft against `plan()`, so
    // adopting their version first would make every field they changed — and this editor only loaded, never
    // touched — look like one of "my changes", and send a stale copy straight back over it. Diffing against
    // what this editor loaded sends only the fields it actually edited; their etag is what lets the write
    // through. That is what frontend.md's "a 409 keeps the editor's changes" has to mean, or keeping mine
    // silently discards theirs.
    if (!this.dirty()) {
      this.plan.set(theirs);
      this.draft.set(draftOf(theirs));
      this.announce('Your changes already match the saved version. Nothing more to save.');
      return;
    }

    await this.save(theirs.etag);
  }

  /** Their saved starting price, for the conflict table. */
  protected savedStartingPrice(plan: MembershipPlan): string {
    return plan.startingPriceCents === null ? '—' : money(plan.startingPriceCents, plan.currency);
  }

  protected useTheirs(theirs: MembershipPlan): void {
    this.plan.set(theirs);
    this.draft.set(draftOf(theirs));
    this.status.set({ kind: 'ready' });
    this.announce('Showing the saved version. Your changes were discarded.');
    this.focus(this.heading);
  }

  private async saveFailed(error: unknown): Promise<void> {
    const problem = problemOf(error);

    if (problem?.code === MEMBERSHIP_CODES.concurrencyConflict && this.plan()) {
      try {
        const theirs = await firstValueFrom(this.memberships.get(this.plan()!.id));
        this.status.set({ kind: 'conflict', theirs });
        this.announce(
          'Someone else saved this tier while you were editing. Compare the two versions.',
        );
        afterNextRender(() => this.conflictBox()?.nativeElement.focus(), {
          injector: this.injector,
        });
        return;
      } catch {
        // Fall through to the plain message; the draft is still in the form.
      }
    }

    this.status.set({ kind: 'ready' });
    this.showErrors(
      fieldErrorsOf(problem),
      messageOf(problem, 'The changes couldn’t be saved. Try again.'),
    );
  }

  private showErrors(errors: FieldErrors, message: string | null): void {
    this.errors.set(errors);
    this.formError.set(message);
    this.announce(message ?? 'Some fields need attention.');
    // The first invalid field takes focus, so its message is read.
    afterNextRender(
      () =>
        this.host.nativeElement
          .querySelector<HTMLElement>('input[aria-invalid="true"], select[aria-invalid="true"]')
          ?.focus(),
      { injector: this.injector },
    );
  }

  private announce(message: string): void {
    this.liveMessage.update((current) => (current === message ? `${message} ` : message));
  }

  private focus(target: () => ElementRef<HTMLElement> | undefined): void {
    afterNextRender(() => target()?.nativeElement.focus(), { injector: this.injector });
  }
}

/** Which field's error a draft key clears, for {@link MembershipPlanEditor.patch}. */
const DRAFT_FIELDS: Partial<Record<keyof Draft, Field>> = {
  name: 'name',
  slug: 'slug',
  perks: 'perks',
  discount: 'discountPercent',
  order: 'displayOrder',
  price: 'startingPriceCents',
  cadence: 'startingCadence',
};

function money(cents: number, currency: string): string {
  return new Intl.NumberFormat('en-US', { style: 'currency', currency }).format(cents / 100);
}

function centsToPrice(cents: number): string {
  return cents % 100 === 0 ? String(cents / 100) : (cents / 100).toFixed(2);
}

function priceToCents(price: string): number | null {
  const trimmed = price.trim();
  if (trimmed === '') return null;
  if (!/^\d+(?:\.\d{1,2})?$/.test(trimmed)) return null;
  return Math.round(Number(trimmed) * 100);
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

function localErrors(draft: Draft): FieldErrors {
  const errors: FieldErrors = {};
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

function fieldErrorsOf(problem: MembershipProblem | null): FieldErrors {
  const errors: FieldErrors = {};
  for (const key of FIELDS) {
    const message = problem?.fieldErrors[key]?.[0];
    if (message) errors[key] = message;
  }
  return errors;
}
