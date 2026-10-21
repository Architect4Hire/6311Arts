import { HttpErrorResponse } from '@angular/common/http';
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
import { ConfirmService } from '../../core/confirm/confirm.service';
import { HasUnsavedChanges, warnBeforeUnload } from '../../core/guards/unsaved-changes.guard';
import { SessionService } from '../../core/session/session.service';
import { STATUS_CHIPS, isSlug, slugFromName } from '../../models/content';
import {
  MEMBERSHIP_CADENCES,
  MEMBERSHIP_CODES,
  MEMBERSHIP_LIMITS,
  MembershipCadence,
  MembershipCommand,
  MembershipPlan,
  MembershipPlanCreate,
  MembershipPlanUpdate,
  MembershipPlainCommand,
  MembershipProblem,
  decodeMembershipProblem,
} from '../../models/membership';
import { ROLES } from '../../models/session';
import { MembershipService } from '../../services/membership.service';
import { PreviewLinkButton } from '../../shared/preview-link';
import { ScheduleControl } from '../../shared/schedule-control';
import { cadenceWords } from './membership-list';

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
  | { readonly kind: 'commanding'; readonly command: MembershipCommand }
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

const COMMAND_LABELS: Readonly<
  Record<MembershipCommand, { readonly busy: string; readonly done: string }>
> = {
  publish: { busy: 'Publishing…', done: 'Published.' },
  unpublish: { busy: 'Unpublishing…', done: 'Unpublished. The tier is a draft again.' },
  archive: { busy: 'Archiving…', done: 'Archived.' },
  schedule: { busy: 'Scheduling…', done: 'Scheduled.' },
  unschedule: { busy: 'Unscheduling…', done: 'Unscheduled. The tier is a draft again.' },
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
    PreviewLinkButton,
    ScheduleControl,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p class="back"><a routerLink="/memberships">← All tiers</a></p>

    <!-- Exists from the start, so what is written into it later is read out. -->
    <p class="sr-only" aria-live="polite">{{ liveMessage() }}</p>

    @switch (status().kind) {
      @case ('loading') {
        <p role="status">Loading tier…</p>
      }
      @case ('load-failed') {
        <ui-notice tone="error" heading="This tier couldn’t be loaded">
          <p>6311arts may be unreachable for a moment.</p>
          <button uiButton type="button" size="sm" (click)="load()">Try again</button>
        </ui-notice>
      }
      @case ('not-found') {
        <ui-notice tone="warning" heading="There’s no such tier">
          <p>It may have been removed, or the link is wrong.</p>
          <a uiButton size="sm" routerLink="/memberships">Back to the tiers</a>
        </ui-notice>
      }
      @default {
        <header class="title">
          <h1 #heading class="cms-title" tabindex="-1">{{ plan()?.name || 'New tier' }}</h1>
          @if (plan(); as plan) {
            <ui-status-chip [tone]="chips[plan.status].tone" [icon]="chips[plan.status].icon">{{
              chips[plan.status].label
            }}</ui-status-chip>
          }
        </header>

        @if (conflict(); as theirs) {
          <div
            #conflictBox
            class="conflict"
            role="region"
            aria-label="Your changes and the saved version"
            tabindex="-1"
          >
            <ui-notice tone="warning" heading="Someone else saved this tier while you were editing">
              <table class="compare">
                <caption class="sr-only">
                  Your changes and the saved version
                </caption>
                <thead>
                  <tr>
                    <th scope="col">Field</th>
                    <th scope="col">Yours</th>
                    <th scope="col">Saved</th>
                  </tr>
                </thead>
                <tbody>
                  <tr>
                    <th scope="row">Name</th>
                    <td>{{ draft().name || '—' }}</td>
                    <td>{{ theirs.name }}</td>
                  </tr>
                  <tr>
                    <th scope="row">Perks</th>
                    <td>{{ perkSummary(cleanPerks(draft().perks)) }}</td>
                    <td>{{ perkSummary(theirs.perks) }}</td>
                  </tr>
                  <tr>
                    <th scope="row">Class discount</th>
                    <td>{{ draft().discount || '0' }}%</td>
                    <td>{{ theirs.discountPercent }}%</td>
                  </tr>
                  <tr>
                    <th scope="row">Order</th>
                    <td>{{ draft().order || '—' }}</td>
                    <td>{{ theirs.displayOrder }}</td>
                  </tr>
                  <tr>
                    <th scope="row">Key</th>
                    <td>{{ draft().slug || '—' }}</td>
                    <td>{{ theirs.slug }}</td>
                  </tr>
                  <tr>
                    <th scope="row">Featured</th>
                    <td>{{ draft().featured ? 'Yes' : 'No' }}</td>
                    <td>{{ theirs.featured ? 'Yes' : 'No' }}</td>
                  </tr>
                  @if (theirs.startingPriceEditable) {
                    <tr>
                      <th scope="row">Starting price</th>
                      <td>{{ draft().price ? '$' + draft().price : '—' }}</td>
                      <td>{{ savedStartingPrice(theirs) }}</td>
                    </tr>
                    <tr>
                      <th scope="row">Billing cadence</th>
                      <td>{{ draft().cadence }}</td>
                      <td>{{ theirs.startingCadence }}</td>
                    </tr>
                  }
                </tbody>
              </table>
              <div class="actions">
                <button uiButton type="button" variant="dark" size="sm" (click)="keepMine(theirs)">
                  Keep my changes
                </button>
                <button uiButton type="button" size="sm" (click)="useTheirs(theirs)">
                  Use the saved version and discard mine
                </button>
              </div>
            </ui-notice>
          </div>
        }

        @if (formError(); as message) {
          <ui-notice tone="error">{{ message }}</ui-notice>
        }

        <div class="layout">
          <form class="form" novalidate (ngSubmit)="save()">
            <fieldset class="fields" [disabled]="busy()">
              <legend class="sr-only">Tier details</legend>
              <ui-field label="Name" [required]="true" [error]="errors().name ?? null">
                <input
                  uiControl
                  name="name"
                  autocomplete="off"
                  [attr.maxlength]="limits.nameMaxLength"
                  [ngModel]="draft().name"
                  (ngModelChange)="setName($event)"
                />
              </ui-field>

              <ui-field
                label="Key"
                [required]="true"
                hint="A stable key for this tier, not a web address: every tier is shown on /memberships."
                [error]="errors().slug ?? null"
              >
                <input
                  uiControl
                  name="slug"
                  autocomplete="off"
                  spellcheck="false"
                  [attr.maxlength]="limits.slugMaxLength"
                  [ngModel]="draft().slug"
                  (ngModelChange)="setSlug($event)"
                />
              </ui-field>

              <div class="pair">
                <ui-field
                  label="Class discount (%)"
                  hint="What a member of this tier saves on every class and workshop."
                  [error]="errors().discountPercent ?? null"
                >
                  <input
                    uiControl
                    name="discountPercent"
                    type="number"
                    inputmode="numeric"
                    min="0"
                    [attr.max]="limits.discountMaxPercent"
                    [ngModel]="draft().discount"
                    (ngModelChange)="setNumber('discount', $event)"
                  />
                </ui-field>
                <ui-field
                  label="Order on the page"
                  hint="1 comes first. A new tier goes last."
                  [error]="errors().displayOrder ?? null"
                >
                  <input
                    uiControl
                    name="displayOrder"
                    type="number"
                    inputmode="numeric"
                    min="1"
                    [attr.max]="limits.displayOrderMax"
                    [ngModel]="draft().order"
                    (ngModelChange)="setNumber('order', $event)"
                  />
                </ui-field>
              </div>

              <label class="choice">
                <input
                  type="checkbox"
                  name="featured"
                  [checked]="draft().featured"
                  (change)="patch({ featured: !draft().featured })"
                />
                Highlight this tier on the memberships page
              </label>
            </fieldset>

            <fieldset
              class="panel"
              [disabled]="busy()"
              [attr.aria-describedby]="errors().perks ? 'perks-error' : null"
            >
              <legend class="panel__title">Perks</legend>
              <p class="panel__hint">
                What the tier includes, in the order the page lists them. At least one is needed to
                publish.
              </p>
              @if (errors().perks; as message) {
                <p id="perks-error" class="error" role="alert">{{ message }}</p>
              }
              <ol class="perks">
                @for (perk of draft().perks; track $index; let index = $index) {
                  <li class="perk">
                    <ui-field [label]="'Perk ' + (index + 1)">
                      <input
                        uiControl
                        [attr.name]="'perk-' + index"
                        autocomplete="off"
                        [attr.maxlength]="limits.perkMaxLength"
                        [ngModel]="perk"
                        [ngModelOptions]="{ standalone: true }"
                        (ngModelChange)="setPerk(index, $event)"
                      />
                    </ui-field>
                    <div class="perk__actions">
                      <button
                        uiButton
                        type="button"
                        size="sm"
                        [disabled]="index === 0"
                        [attr.aria-label]="'Move perk ' + (index + 1) + ' up'"
                        (click)="movePerk(index, -1)"
                      >
                        ↑
                      </button>
                      <button
                        uiButton
                        type="button"
                        size="sm"
                        [disabled]="index === draft().perks.length - 1"
                        [attr.aria-label]="'Move perk ' + (index + 1) + ' down'"
                        (click)="movePerk(index, 1)"
                      >
                        ↓
                      </button>
                      <button
                        uiButton
                        type="button"
                        size="sm"
                        [attr.aria-label]="'Remove perk ' + (index + 1)"
                        (click)="removePerk(index)"
                      >
                        Remove
                      </button>
                    </div>
                  </li>
                }
              </ol>
              <div class="actions">
                <button
                  uiButton
                  type="button"
                  size="sm"
                  [disabled]="draft().perks.length >= limits.perksMaxCount"
                  (click)="addPerk()"
                >
                  Add a perk
                </button>
              </div>
            </fieldset>

            <fieldset class="panel" [disabled]="busy()">
              <legend class="panel__title">Price and billing</legend>
              @if (priceEditable()) {
                <div class="pair">
                  <ui-field
                    label="Price"
                    hint="What a member pays each time it bills. Needed to publish."
                    [error]="errors().startingPriceCents ?? null"
                  >
                    <input
                      uiControl
                      name="price"
                      type="number"
                      inputmode="decimal"
                      min="0"
                      step="0.01"
                      [attr.max]="limits.startingPriceMaxCents / 100"
                      [ngModel]="draft().price"
                      (ngModelChange)="setNumber('price', $event)"
                    />
                  </ui-field>
                  <ui-field
                    label="Bills"
                    hint="Sent to Square once, when the plan is created there."
                    [error]="errors().startingCadence ?? null"
                  >
                    <select
                      uiControl
                      name="startingCadence"
                      [ngModel]="draft().cadence"
                      (ngModelChange)="patch({ cadence: $event })"
                    >
                      @for (option of cadences; track option.value) {
                        <option [value]="option.value">{{ option.label }}</option>
                      }
                    </select>
                  </ui-field>
                </div>
              } @else if (plan(); as plan) {
                <p class="panel__text price">{{ squarePrice(plan) }}</p>
                <p class="panel__text">{{ squareCadence(plan) }}</p>
                <p id="square-holds-hint" class="panel__hint">
                  Square holds this tier’s price and billing now. Change them in Square; the CMS
                  keeps the name, the perks and the class discount.
                </p>
              }
            </fieldset>

            <div class="actions">
              <button
                uiButton
                type="submit"
                variant="dark"
                [busy]="status().kind === 'saving'"
                [disabled]="!dirty() || busy()"
              >
                {{ saveLabel() }}
              </button>
              @if (plan() && dirty()) {
                <button uiButton type="button" [disabled]="busy()" (click)="discard()">
                  Discard changes
                </button>
              }
            </div>
          </form>

          @if (plan(); as plan) {
            <aside class="side">
              <section class="panel" aria-labelledby="publishing-heading">
                <h2 #publishingHeading id="publishing-heading" class="panel__title" tabindex="-1">
                  Publishing
                </h2>
                <p class="panel__text">{{ statusText(plan) }}</p>
                @if (dirty()) {
                  <p class="panel__hint" id="publishing-hint">
                    Save your changes first: these act on the saved version.
                  </p>
                }
                <div class="actions">
                  @if (canPublish()) {
                    <button
                      uiButton
                      type="button"
                      variant="dark"
                      size="sm"
                      aria-describedby="publishing-hint"
                      [busy]="commanding() === 'publish'"
                      [disabled]="dirty() || busy()"
                      (click)="run('publish')"
                    >
                      Publish
                    </button>
                  }
                  @if (plan.status === 'Published') {
                    <button
                      uiButton
                      type="button"
                      size="sm"
                      aria-describedby="publishing-hint"
                      [busy]="commanding() === 'unpublish'"
                      [disabled]="dirty() || busy()"
                      (click)="unpublish()"
                    >
                      Unpublish
                    </button>
                  }
                  @if (canArchive()) {
                    <button
                      uiButton
                      type="button"
                      size="sm"
                      aria-describedby="publishing-hint"
                      [busy]="commanding() === 'archive'"
                      [disabled]="dirty() || busy()"
                      (click)="archive()"
                    >
                      Archive
                    </button>
                  }
                </div>

                <cms-schedule-control
                  noun="tier"
                  [status]="plan.status"
                  [scheduledFor]="plan.scheduledFor"
                  [disabled]="dirty() || busy()"
                  [busy]="commanding()"
                  (schedule)="schedule($event)"
                  (unschedule)="unschedule()"
                />

                <cms-preview-link
                  entityType="membership-plan"
                  [entityId]="plan.id"
                  [dirty]="dirty()"
                />
              </section>
            </aside>
          }
        </div>
      }
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-5);
    }
    .back {
      margin: 0;
      font-size: var(--fs-small);
    }
    .title {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3);
      align-items: center;
    }
    .title .cms-title {
      margin: 0;
    }
    .cms-title:focus,
    .conflict:focus,
    .panel__title:focus {
      outline: none;
    }
    .layout {
      display: grid;
      grid-template-columns: minmax(0, 3fr) minmax(min(100%, 240px), 1fr);
      gap: var(--space-6);
      align-items: start;
    }
    @media (max-width: 900px) {
      .layout {
        grid-template-columns: minmax(0, 1fr);
      }
    }
    .form,
    .side,
    .fields {
      display: grid;
      gap: var(--space-4);
      min-width: 0;
    }
    .fields {
      margin: 0;
      padding: 0;
      border: 0;
    }
    .pair {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 180px), 1fr));
      gap: var(--space-4);
    }
    .panel {
      display: grid;
      gap: var(--space-3);
      padding: var(--space-4);
      border: var(--border-thin) solid var(--line);
    }
    .panel__title {
      margin: 0;
      font-size: var(--fs-label);
      font-weight: var(--fw-heavy);
      letter-spacing: var(--track-label);
      text-transform: uppercase;
    }
    .panel__text,
    .panel__hint {
      margin: 0;
    }
    .panel__hint {
      font-size: var(--fs-small);
      color: var(--text-soft);
    }
    .price {
      font-weight: var(--fw-bold);
    }
    .perks {
      display: grid;
      gap: var(--space-3);
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .perk {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      align-items: flex-end;
    }
    .perk ui-field {
      flex: 1 1 240px;
      min-width: 0;
    }
    .perk__actions {
      display: flex;
      gap: var(--space-2);
    }
    .choice {
      display: flex;
      gap: var(--space-2);
      align-items: baseline;
    }
    .error {
      margin: 0;
      color: var(--danger);
      font-size: var(--fs-small);
      font-weight: var(--fw-bold);
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
    .compare {
      width: 100%;
      border-collapse: collapse;
      font-size: var(--fs-small);
    }
    .compare th,
    .compare td {
      padding: var(--space-1) var(--space-2);
      border-bottom: var(--border-thin) solid var(--line);
      text-align: left;
      vertical-align: top;
      overflow-wrap: anywhere;
    }
  `,
})
export class MembershipPlanEditor implements HasUnsavedChanges {
  /** The route's `:id`; absent on `/memberships/new`. */
  readonly id = input<string>();

  private readonly memberships = inject(MembershipService);
  private readonly confirm = inject(ConfirmService);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly conflictBox = viewChild<ElementRef<HTMLElement>>('conflictBox');
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly publishingHeading = viewChild<ElementRef<HTMLElement>>('publishingHeading');

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

  protected readonly isAdmin = computed(() => this.session.hasAnyRole([ROLES.admin]));
  protected readonly busy = computed(() => {
    const kind = this.status().kind;
    return kind === 'saving' || kind === 'commanding';
  });
  protected readonly commanding = computed(() => {
    const status = this.status();
    return status.kind === 'commanding' ? status.command : null;
  });
  protected readonly conflict = computed(() => {
    const status = this.status();
    return status.kind === 'conflict' ? status.theirs : null;
  });

  /** ADR 0005: the price and the cadence are ordinary fields only until Square holds the plan. */
  protected readonly priceEditable = computed(() => this.plan()?.startingPriceEditable ?? true);

  protected readonly canPublish = computed(() => {
    const status = this.plan()?.status;
    return status === 'Draft' || status === 'Scheduled';
  });
  protected readonly canArchive = computed(() => {
    const status = this.plan()?.status;
    return this.isAdmin() && status !== undefined && status !== 'Archived';
  });

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
        Object.fromEntries(Object.entries(errors).filter(([key]) => !cleared.includes(key as Field))),
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

  protected statusText(plan: MembershipPlan): string {
    switch (plan.status) {
      case 'Published':
        return 'Published: on the memberships page once the site reads from the CMS.';
      case 'Archived':
        return 'Archived: retired from the site. Archiving can’t be undone here.';
      case 'Scheduled':
        return 'Scheduled to publish.';
      default:
        return plan.publishedAt
          ? 'A draft again, after being published.'
          : 'A draft: only staff can see it. Publishing needs a price and at least one perk.';
    }
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
    return plan.startingPriceCents === null
      ? '—'
      : money(plan.startingPriceCents, plan.currency);
  }

  protected useTheirs(theirs: MembershipPlan): void {
    this.plan.set(theirs);
    this.draft.set(draftOf(theirs));
    this.status.set({ kind: 'ready' });
    this.announce('Showing the saved version. Your changes were discarded.');
    this.focus(this.heading);
  }

  protected async unpublish(): Promise<void> {
    const confirmed = await this.confirm.confirm({
      heading: 'Unpublish this tier?',
      message:
        'It goes back to being a draft and leaves the memberships page. Members who already joined are unaffected: Square owns their subscriptions.',
      confirmLabel: 'Unpublish',
      cancelLabel: 'Keep published',
    });
    if (confirmed) await this.run('unpublish');
  }

  protected async archive(): Promise<void> {
    const confirmed = await this.confirm.confirm({
      heading: 'Archive this tier?',
      message:
        'An archived tier is retired from the site, and archiving can’t be undone from the CMS. The archive is recorded under your name.',
      confirmLabel: 'Archive tier',
      cancelLabel: 'Keep it',
    });
    if (confirmed) await this.run('archive');
  }

  /** The `schedule` command: the only one with a body, so it does not go through {@link run} (ADR 0036). */
  protected async schedule(scheduledFor: string): Promise<void> {
    const plan = this.plan();
    if (!plan || this.busy() || this.dirty()) return;

    this.errors.set({});
    this.formError.set(null);
    this.status.set({ kind: 'commanding', command: 'schedule' });
    this.announce(COMMAND_LABELS.schedule.busy);
    try {
      const updated = await firstValueFrom(
        this.memberships.schedule(plan.id, scheduledFor, plan.etag),
      );
      this.plan.set(updated);
      this.draft.set(draftOf(updated));
      this.status.set({ kind: 'ready' });
      this.announce(COMMAND_LABELS.schedule.done);
    } catch (error: unknown) {
      await this.commandFailed(error);
    }
  }

  protected async unschedule(): Promise<void> {
    await this.run('unschedule');
  }

  protected async run(command: MembershipPlainCommand): Promise<void> {
    const plan = this.plan();
    if (!plan || this.busy() || this.dirty()) return;

    this.errors.set({});
    this.formError.set(null);
    this.status.set({ kind: 'commanding', command });
    this.announce(COMMAND_LABELS[command].busy);
    try {
      const updated = await firstValueFrom(this.memberships.command(plan.id, command, plan.etag));
      this.plan.set(updated);
      this.draft.set(draftOf(updated));
      this.status.set({ kind: 'ready' });
      this.announce(COMMAND_LABELS[command].done);
      this.focus(this.publishingHeading);
    } catch (error: unknown) {
      await this.commandFailed(error);
    }
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

  private async commandFailed(error: unknown): Promise<void> {
    const problem = problemOf(error);
    const stale =
      problem?.code === MEMBERSHIP_CODES.concurrencyConflict ||
      problem?.code === MEMBERSHIP_CODES.transitionInvalid;

    if (stale && this.plan()) {
      try {
        const latest = await firstValueFrom(this.memberships.get(this.plan()!.id));
        this.plan.set(latest);
        this.draft.set(draftOf(latest));
      } catch {
        // The message below still says what happened.
      }
    }

    this.status.set({ kind: 'ready' });
    this.showErrors(
      fieldErrorsOf(problem),
      problem?.code === MEMBERSHIP_CODES.concurrencyConflict
        ? 'Someone else changed this tier a moment ago. Here is the latest version; check it and try again.'
        : messageOf(problem, 'That didn’t work. Try again.'),
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

function problemOf(error: unknown): MembershipProblem | null {
  return error instanceof HttpErrorResponse
    ? decodeMembershipProblem(error.error, error.status)
    : null;
}

function fieldErrorsOf(problem: MembershipProblem | null): FieldErrors {
  const errors: FieldErrors = {};
  for (const key of FIELDS) {
    const message = problem?.fieldErrors[key]?.[0];
    if (message) errors[key] = message;
  }
  return errors;
}

function messageOf(problem: MembershipProblem | null, fallback: string): string {
  if (problem === null || problem.status === 0)
    return 'Can’t reach 6311arts. Your changes are still here; try again.';
  const message = problem.detail ?? fallback;

  // A 5xx says nothing the editor can act on, so hand them the one thing support can: the correlation id,
  // which is this request's trace id (ADR 0019). Appended here rather than left to the global problem toast,
  // because these calls deliberately suppress it to show the failure in place.
  return problem.status >= 500 && problem.correlationId !== null
    ? `${message} Quote reference ${problem.correlationId} if you report it.`
    : message;
}
