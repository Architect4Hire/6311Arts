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
import { ROLES } from '../../models/session';
import {
  SHOP_CATEGORIES,
  SHOP_CODES,
  SHOP_LIMITS,
  ShopCommand,
  ShopItem,
  ShopItemCreate,
  ShopItemUpdate,
  ShopProblem,
  categoryLabel,
  decodeShopProblem,
  isSku,
  normalizeSku,
} from '../../models/shop';
import { ShopService } from '../../services/shop.service';
import { ArtistPicker, PickedArtist } from '../../shared/artist-picker';
import { PreviewLinkButton } from '../../shared/preview-link';
import { RichTextEditor } from '../../shared/rich-text-editor';
import { ScheduleControl } from '../../shared/schedule-control';
import { GalleryEditor } from './gallery-editor';

interface Draft {
  readonly title: string;
  readonly slug: string;
  /** Whether the person has typed a slug; until then it follows the title (new pieces only). */
  readonly slugEdited: boolean;
  /** HTML, as the rich-text editor holds it. */
  readonly description: string;
  readonly artist: PickedArtist | null;
  readonly categories: readonly string[];
  readonly images: readonly string[];
  readonly sku: string;
  readonly oneOfAKind: boolean;
  /** The starting price in whole currency units, as typed; empty when there is none. */
  readonly price: string;
  /** The starting count, as typed. */
  readonly quantity: string;
}

type Status =
  | { readonly kind: 'loading' }
  | { readonly kind: 'load-failed' }
  | { readonly kind: 'not-found' }
  | { readonly kind: 'ready' }
  | { readonly kind: 'saving' }
  | { readonly kind: 'commanding'; readonly command: ShopCommand }
  | { readonly kind: 'conflict'; readonly theirs: ShopItem };

type FieldName =
  | 'title'
  | 'slug'
  | 'description'
  | 'artistId'
  | 'categories'
  | 'imageMediaIds'
  | 'sku'
  | 'oneOfAKind'
  | 'startingPriceCents'
  | 'startingStockQuantity';

type FieldErrors = Partial<Record<FieldName, string>>;

const EMPTY: Draft = {
  title: '',
  slug: '',
  slugEdited: false,
  description: '',
  artist: null,
  categories: [],
  images: [],
  sku: '',
  oneOfAKind: true,
  price: '',
  quantity: '1',
};

/** Router state set by "Create piece", read by the editor it navigates to. */
const CREATED_STATE = 'shopItemCreated';

const COMMAND_LABELS: Readonly<
  Record<ShopCommand, { readonly busy: string; readonly done: string }>
> = {
  publish: { busy: 'Publishing…', done: 'Published.' },
  unpublish: { busy: 'Unpublishing…', done: 'Unpublished. The piece is a draft again.' },
  archive: { busy: 'Archiving…', done: 'Archived.' },
  schedule: { busy: 'Scheduling…', done: 'Scheduled.' },
  unschedule: { busy: 'Unscheduling…', done: 'Unscheduled. The piece is a draft again.' },
};

/**
 * One piece of artwork (`/admin/shop/new`, `/admin/shop/:id`): its copy, its artist, its categories, its
 * photographs and its SKU, and then publish, schedule, unpublish or (Admins) archive.
 *
 * **What the CMS owns and what Square does** (square.md, ADR 0032, 0038). The copy, the artist, the categories, the
 * photographs and the SKU are the CMS's, and so are the starting price and count — but only until Square reports a
 * price or stock. After that the figures shown are Square's, with the change-price and set-quantity commands
 * arriving in 5.4a and 5.4b, and a one-of-a-kind piece never gets the second of those.
 *
 * Unsaved edits survive every recoverable failure, and a 409 shows the saved version beside the editor's own
 * (frontend.md). Status commands act on the saved version, so they wait until the form is saved.
 */
@Component({
  selector: 'cms-shop-item-editor',
  imports: [
    FormsModule,
    RouterLink,
    ArtistPicker,
    GalleryEditor,
    PreviewLinkButton,
    RichTextEditor,
    ScheduleControl,
    UiButton,
    UiControl,
    UiField,
    UiNotice,
    UiStatusChip,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p class="back"><a routerLink="/shop">← All artwork</a></p>

    <!-- Exists from the start, so what is written into it later is read out. -->
    <p class="sr-only" aria-live="polite">{{ liveMessage() }}</p>

    @switch (status().kind) {
      @case ('loading') {
        <p role="status">Loading piece…</p>
      }
      @case ('load-failed') {
        <ui-notice tone="error" heading="This piece couldn’t be loaded">
          <p>6311arts may be unreachable for a moment.</p>
          <button uiButton type="button" size="sm" (click)="load()">Try again</button>
        </ui-notice>
      }
      @case ('not-found') {
        <ui-notice tone="warning" heading="There’s no such piece">
          <p>It may have been removed, or the link is wrong.</p>
          <a uiButton size="sm" routerLink="/shop">Back to the shop</a>
        </ui-notice>
      }
      @default {
        <header class="title">
          <h1 #heading class="cms-title" tabindex="-1">{{ item()?.title || 'New piece' }}</h1>
          @if (item(); as piece) {
            <ui-status-chip [tone]="chips[piece.status].tone" [icon]="chips[piece.status].icon">{{
              chips[piece.status].label
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
            <ui-notice tone="warning" heading="Someone else saved this piece while you were editing">
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
                    <th scope="row">Title</th>
                    <td>{{ draft().title || '—' }}</td>
                    <td>{{ theirs.title }}</td>
                  </tr>
                  <tr>
                    <th scope="row">Web address</th>
                    <td>/shop/{{ draft().slug }}</td>
                    <td>/shop/{{ theirs.slug }}</td>
                  </tr>
                  <tr>
                    <th scope="row">Artist</th>
                    <td>{{ draft().artist?.name || 'Nobody' }}</td>
                    <td>{{ theirs.artist?.name || 'Nobody' }}</td>
                  </tr>
                  <tr>
                    <th scope="row">SKU</th>
                    <td>{{ draft().sku || '—' }}</td>
                    <td>{{ theirs.sku }}</td>
                  </tr>
                  <tr>
                    <th scope="row">Categories</th>
                    <td>{{ labels(draft().categories) }}</td>
                    <td>{{ labels(theirs.categories) }}</td>
                  </tr>
                  <tr>
                    <th scope="row">Photographs</th>
                    <td>{{ draft().images.length }}</td>
                    <td>{{ theirs.imageMediaIds.length }}</td>
                  </tr>
                  <tr>
                    <th scope="row">Description</th>
                    <td>{{ draft().description.length }} characters</td>
                    <td>{{ (theirs.description ?? '').length }} characters</td>
                  </tr>
                  @if (theirs.startingPriceEditable) {
                    <tr>
                      <th scope="row">Starting price</th>
                      <td>{{ draft().price ? '$' + draft().price : '—' }}</td>
                      <td>{{ savedStartingPrice(theirs) }}</td>
                    </tr>
                  }
                  @if (theirs.startingStockEditable) {
                    <tr>
                      <th scope="row">One of a kind</th>
                      <td>{{ draft().oneOfAKind ? 'Yes' : 'No' }}</td>
                      <td>{{ theirs.oneOfAKind ? 'Yes' : 'No' }}</td>
                    </tr>
                    <tr>
                      <th scope="row">Starting count</th>
                      <td>{{ draft().oneOfAKind ? 'One' : draft().quantity || '—' }}</td>
                      <td>{{ theirs.oneOfAKind ? 'One' : (theirs.startingStockQuantity ?? '—') }}</td>
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
            <!-- Disabled while a save or command is in flight, so nothing typed then is overwritten by its answer. -->
            <fieldset class="fields" [disabled]="busy()">
              <legend class="sr-only">Piece details</legend>
              <ui-field label="Title" [required]="true" [error]="errors().title ?? null">
                <input
                  uiControl
                  name="title"
                  autocomplete="off"
                  [attr.maxlength]="limits.titleMaxLength"
                  [ngModel]="draft().title"
                  (ngModelChange)="setTitle($event)"
                />
              </ui-field>

              <ui-field
                label="Web address"
                [required]="true"
                [hint]="slugHint()"
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

              <ui-field
                label="SKU"
                [hint]="skuHint()"
                [error]="errors().sku ?? null"
              >
                <input
                  uiControl
                  name="sku"
                  autocomplete="off"
                  spellcheck="false"
                  [attr.maxlength]="limits.skuMaxLength"
                  [ngModel]="draft().sku"
                  (ngModelChange)="patch({ sku: $event })"
                />
              </ui-field>
            </fieldset>

            <cms-rich-text
              label="Description"
              hint="Headings, bold, italic and lists; the item's pictures are its photographs. Needed to publish."
              [value]="draft().description"
              (valueChange)="patch({ description: $event })"
              [disabled]="busy()"
              [error]="errors().description ?? null"
            />

            <fieldset
              class="panel"
              [disabled]="busy()"
              [attr.aria-describedby]="errors().categories ? 'categories-error' : null"
            >
              <legend class="panel__title">Categories</legend>
              @if (errors().categories; as message) {
                <p id="categories-error" class="error" role="alert">{{ message }}</p>
              }
              <div class="choices">
                @for (option of categories; track option.slug) {
                  <label class="choice">
                    <input
                      type="checkbox"
                      [attr.name]="'category-' + option.slug"
                      [checked]="draft().categories.includes(option.slug)"
                      (change)="toggleCategory(option.slug)"
                    />
                    {{ option.label }}
                  </label>
                }
              </div>
            </fieldset>

            <fieldset
              class="panel"
              [disabled]="busy()"
              [attr.aria-describedby]="errors().oneOfAKind ? 'one-of-a-kind-error' : null"
            >
              <legend class="panel__title">Price and stock</legend>
              @if (priceEditable()) {
                <div class="pair">
                  <ui-field
                    label="Price"
                    [hint]="priceHint()"
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
                      (ngModelChange)="setPrice($event)"
                    />
                  </ui-field>
                  @if (!draft().oneOfAKind) {
                    <ui-field
                      label="How many"
                      hint="How many there are to sell."
                      [error]="errors().startingStockQuantity ?? null"
                    >
                      <input
                        uiControl
                        name="quantity"
                        type="number"
                        inputmode="numeric"
                        min="1"
                        [attr.max]="limits.startingStockMaxQuantity"
                        [ngModel]="draft().quantity"
                        (ngModelChange)="setQuantity($event)"
                      />
                    </ui-field>
                  }
                </div>
                <label class="choice">
                  <input
                    type="checkbox"
                    name="oneOfAKind"
                    [checked]="draft().oneOfAKind"
                    (change)="toggleOneOfAKind()"
                  />
                  One of a kind — there is only one of it
                </label>
                @if (errors().oneOfAKind; as message) {
                  <p id="one-of-a-kind-error" class="error" role="alert">{{ message }}</p>
                }
              } @else if (item(); as piece) {
                <p class="panel__text price">{{ squarePrice(piece) }}</p>
                <p class="panel__text">{{ squareStock(piece) }}</p>
                <p id="square-holds-hint" class="panel__hint">
                  Square holds this piece’s price and stock now. Changing either is an audited
                  request to Square, which arrives with the Square sync.
                  @if (piece.oneOfAKind) {
                    There is only one of this piece, so its stock is never set from here.
                  }
                </p>
                <!-- 5.4a and 5.4b send these as commands with the figures staff last saw (ADR 0032). -->
                <div class="actions">
                  <button
                    uiButton
                    type="button"
                    size="sm"
                    [disabled]="true"
                    aria-describedby="square-holds-hint"
                  >
                    Change price
                  </button>
                  @if (!piece.oneOfAKind) {
                    <button
                      uiButton
                      type="button"
                      size="sm"
                      [disabled]="true"
                      aria-describedby="square-holds-hint"
                    >
                      Set quantity
                    </button>
                  }
                </div>
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
              @if (item() && dirty()) {
                <button uiButton type="button" [disabled]="busy()" (click)="discard()">
                  Discard changes
                </button>
              }
            </div>
          </form>

          <aside class="side">
            <cms-artist-picker
              [artist]="draft().artist"
              (artistChange)="patch({ artist: $event })"
              [disabled]="busy()"
              [error]="errors().artistId ?? null"
            />

            <cms-gallery-editor
              [images]="draft().images"
              (imagesChange)="patch({ images: $event })"
              [disabled]="busy()"
              [error]="errors().imageMediaIds ?? null"
            />

            @if (item(); as piece) {
              <section class="panel" aria-labelledby="publishing-heading">
                <h2 #publishingHeading id="publishing-heading" class="panel__title" tabindex="-1">
                  Publishing
                </h2>
                <p class="panel__text">{{ statusText(piece) }}</p>
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
                      [attr.aria-describedby]="dirty() ? 'publishing-hint' : null"
                      [busy]="commanding() === 'publish'"
                      [disabled]="dirty() || busy()"
                      (click)="run('publish')"
                    >
                      Publish
                    </button>
                  }
                  @if (piece.status === 'Published') {
                    <button
                      uiButton
                      type="button"
                      size="sm"
                      [attr.aria-describedby]="dirty() ? 'publishing-hint' : null"
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
                      [attr.aria-describedby]="dirty() ? 'publishing-hint' : null"
                      [busy]="commanding() === 'archive'"
                      [disabled]="dirty() || busy()"
                      (click)="archive()"
                    >
                      Archive
                    </button>
                  }
                </div>

                <cms-schedule-control
                  noun="piece"
                  [status]="piece.status"
                  [scheduledFor]="piece.scheduledFor"
                  [disabled]="dirty() || busy()"
                  [busy]="commanding()"
                  (schedule)="schedule($event)"
                  (unschedule)="unschedule()"
                />

                <cms-preview-link entityType="shop-item" [entityId]="piece.id" [dirty]="dirty()" />
              </section>
            }
          </aside>
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
      grid-template-columns: minmax(0, 3fr) minmax(min(100%, 280px), 1fr);
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
      margin: 0;
      padding: var(--space-4);
      border: var(--border-thin) solid var(--line);
    }
    .panel__title {
      margin: 0;
      padding: 0;
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
    .choices {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 180px), 1fr));
      gap: var(--space-2);
    }
    .choice {
      display: flex;
      gap: var(--space-2);
      align-items: baseline;
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
    .error {
      margin: 0;
      color: var(--danger);
      font-weight: var(--fw-bold);
    }
  `,
})
export class ShopItemEditor implements HasUnsavedChanges {
  /** The route's `:id`; absent on `/shop/new`. */
  readonly id = input<string>();

  private readonly shop = inject(ShopService);
  private readonly confirm = inject(ConfirmService);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly conflictBox = viewChild<ElementRef<HTMLElement>>('conflictBox');
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly publishingHeading = viewChild<ElementRef<HTMLElement>>('publishingHeading');
  private readonly descriptionEditor = viewChild(RichTextEditor);

  private readonly justCreated =
    this.router.currentNavigation()?.extras.state?.[CREATED_STATE] === true;

  protected readonly chips = STATUS_CHIPS;
  protected readonly limits = SHOP_LIMITS;
  protected readonly categories = SHOP_CATEGORIES;

  protected readonly status = signal<Status>({ kind: 'loading' });
  /** The piece as saved; `null` while creating one. */
  protected readonly item = signal<ShopItem | null>(null);
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
  protected readonly slugLocked = computed(() => this.item()?.slugLocked ?? false);
  protected readonly canPublish = computed(() => {
    const status = this.item()?.status;
    return status === 'Draft' || status === 'Scheduled';
  });
  protected readonly canArchive = computed(() => {
    const status = this.item()?.status;
    return this.isAdmin() && status !== undefined && status !== 'Archived';
  });

  /** ADR 0032, 0038: the starting figures are ordinary fields only until Square holds the record. */
  protected readonly priceEditable = computed(() => this.item()?.startingPriceEditable ?? true);

  protected readonly saveLabel = computed(() => {
    if (this.status().kind === 'saving') return this.item() ? 'Saving…' : 'Creating…';
    return this.item() ? 'Save' : 'Create piece';
  });

  protected readonly priceHint = computed(
    () => `What it sells for, in ${this.item()?.currency ?? 'USD'}. Needed to publish.`,
  );

  protected readonly skuHint = computed(() => {
    const saved = this.item();
    if (!saved) return 'Left empty, the shop suggests one (for example ART-17). Unique across the CMS.';
    return `Unique across the whole CMS, class sessions included. Suggested as ART-${saved.itemNumber}.`;
  });

  /**
   * Once published, the address is someone's bookmark: changing it is allowed, and the old address is redirected to
   * the new one when it is saved (ADR 0035). The hint says so, and names the address that will redirect.
   */
  protected readonly slugHint = computed(() => {
    const saved = this.item();
    const slug = this.draft().slug;
    if (!this.slugLocked() || !saved) {
      return `6311arts.com/shop/${slug || '…'} — lower-case letters, digits and hyphens.`;
    }
    return slug === saved.slug
      ? `6311arts.com/shop/${slug} — published, so changing it sends visitors from this address to the new one.`
      : `6311arts.com/shop/${slug || '…'} — on saving, visitors to /shop/${saved.slug} are sent here.`;
  });

  /** Edits not yet saved: the unsaved-changes guard asks before they are lost. */
  readonly dirty = computed(() => {
    const draft = this.draft();
    const item = this.item();
    if (!item) {
      return (
        draft.title.trim() !== '' ||
        draft.description !== '' ||
        draft.artist !== null ||
        draft.categories.length > 0 ||
        draft.images.length > 0 ||
        draft.sku.trim() !== '' ||
        draft.price.trim() !== ''
      );
    }
    return Object.keys(changesOf(item, draft)).length > 0;
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
      this.item.set(null);
      this.draft.set(EMPTY);
      this.status.set({ kind: 'ready' });
      return;
    }

    this.status.set({ kind: 'loading' });
    try {
      const piece = await firstValueFrom(this.shop.get(id));
      this.item.set(piece);
      this.draft.set(draftOf(piece));
      this.status.set({ kind: 'ready' });
      if (this.justCreated) {
        this.announce('Piece created. Publish it when it’s ready.');
        this.focus(this.heading);
      }
    } catch (error: unknown) {
      this.status.set(
        problemOf(error)?.code === SHOP_CODES.notFound
          ? { kind: 'not-found' }
          : { kind: 'load-failed' },
      );
    }
  }

  protected patch(change: Partial<Draft>): void {
    this.draft.update((draft) => ({ ...draft, ...change }));
    const cleared = Object.keys(change)
      .map((key) => (key === 'artist' ? 'artistId' : key === 'images' ? 'imageMediaIds' : key))
      .filter((key) => key in this.errors());
    if (cleared.length > 0) {
      this.errors.update((errors) =>
        Object.fromEntries(Object.entries(errors).filter(([key]) => !cleared.includes(key))),
      );
    }
  }

  protected setTitle(title: string): void {
    const followsTitle = !this.item() && !this.draft().slugEdited;
    this.patch({ title, ...(followsTitle ? { slug: slugFromName(title) } : {}) });
  }

  protected setSlug(slug: string): void {
    this.patch({ slug, slugEdited: true });
  }

  /** A number input's value accessor hands over a number, or `null` for something that is not one. */
  protected setPrice(value: string | number | null): void {
    this.patch({ price: value === null || value === undefined ? '' : String(value) });
  }

  protected setQuantity(value: string | number | null): void {
    this.patch({ quantity: value === null || value === undefined ? '' : String(value) });
  }

  protected toggleCategory(slug: string): void {
    const chosen = this.draft().categories;
    this.patch({
      categories: chosen.includes(slug)
        ? chosen.filter((candidate) => candidate !== slug)
        : [...chosen, slug],
    });
  }

  /** One of a kind means a count of one, so the field goes and the number follows (ADR 0038). */
  protected toggleOneOfAKind(): void {
    const oneOfAKind = !this.draft().oneOfAKind;
    this.patch({ oneOfAKind, ...(oneOfAKind ? { quantity: '1' } : {}) });
  }

  protected labels(categories: readonly string[]): string {
    return categories.length === 0 ? 'None' : categories.map(categoryLabel).join(', ');
  }

  protected squarePrice(piece: ShopItem): string {
    return piece.priceCents === null
      ? 'Square has not reported a price yet.'
      : `Price in Square: ${money(piece.priceCents, piece.currency)}`;
  }

  protected squareStock(piece: ShopItem): string {
    if (piece.soldOut) return 'Sold out in Square.';
    return piece.stockOnHand === null
      ? 'Square has not reported stock yet.'
      : `${piece.stockOnHand} in stock in Square.`;
  }

  protected discard(): void {
    const piece = this.item();
    if (piece) this.draft.set(draftOf(piece));
    this.errors.set({});
    this.formError.set(null);
  }

  protected statusText(piece: ShopItem): string {
    switch (piece.status) {
      case 'Published':
        return 'Published: in the shop. Edits show there within a minute.';
      case 'Archived':
        return 'Archived: retired from the shop. Archiving can’t be undone here.';
      case 'Scheduled':
        return 'Scheduled to publish.';
      default:
        return piece.publishedAt
          ? 'A draft again, after being published. Its web address stays as it is.'
          : 'A draft: only staff can see it. Publishing needs a description, a price and a described photograph.';
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

    const piece = this.item();
    this.errors.set({});
    this.formError.set(null);
    this.status.set({ kind: 'saving' });
    try {
      if (!piece) {
        const created = await firstValueFrom(this.shop.create(createOf(draft)));
        this.item.set(created);
        this.draft.set(draftOf(created));
        this.status.set({ kind: 'ready' });
        await this.router.navigate(['/shop', created.id], {
          replaceUrl: true,
          state: { [CREATED_STATE]: true },
        });
        return;
      }

      const saved = await firstValueFrom(
        this.shop.update(piece.id, changesOf(piece, draft), etag ?? piece.etag),
      );
      this.item.set(saved);
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

  protected async keepMine(theirs: ShopItem): Promise<void> {
    this.status.set({ kind: 'ready' });
    this.focus(this.heading);

    // Deliberately *not* `item.set(theirs)` before saving. `save` diffs the draft against `item()`, so
    // adopting their version first would make every field they changed — and this editor only loaded, never
    // touched — look like one of "my changes", and send a stale copy straight back over it. Diffing against
    // what this editor loaded sends only the fields it actually edited; their etag is what lets the write
    // through. That is what frontend.md's "a 409 keeps the editor's changes" has to mean, or keeping mine
    // silently discards theirs.
    if (!this.dirty()) {
      this.item.set(theirs);
      this.draft.set(draftOf(theirs));
      this.announce('Your changes already match the saved version. Nothing more to save.');
      return;
    }

    await this.save(theirs.etag);
  }

  /** Their saved starting price, for the conflict table. */
  protected savedStartingPrice(piece: ShopItem): string {
    return piece.startingPriceCents === null
      ? '—'
      : money(piece.startingPriceCents, piece.currency);
  }

  protected useTheirs(theirs: ShopItem): void {
    this.item.set(theirs);
    this.draft.set(draftOf(theirs));
    this.status.set({ kind: 'ready' });
    this.announce('Showing the saved version. Your changes were discarded.');
    this.focus(this.heading);
  }

  protected async unpublish(): Promise<void> {
    const confirmed = await this.confirm.confirm({
      heading: 'Unpublish this piece?',
      message:
        'It goes back to being a draft and leaves the shop. Its web address stays the same, so publishing again restores it.',
      confirmLabel: 'Unpublish',
      cancelLabel: 'Keep published',
    });
    if (confirmed) await this.run('unpublish');
  }

  protected async archive(): Promise<void> {
    const confirmed = await this.confirm.confirm({
      heading: 'Archive this piece?',
      message:
        'An archived piece is retired from the shop, and archiving can’t be undone from the CMS. The archive is recorded under your name.',
      confirmLabel: 'Archive piece',
      cancelLabel: 'Keep it',
    });
    if (confirmed) await this.run('archive');
  }

  /** The `schedule` command: the only one with a body, so it does not go through {@link run} (ADR 0036). */
  protected async schedule(scheduledFor: string): Promise<void> {
    const piece = this.item();
    if (!piece || this.busy() || this.dirty()) return;

    this.errors.set({});
    this.formError.set(null);
    this.status.set({ kind: 'commanding', command: 'schedule' });
    this.announce(COMMAND_LABELS.schedule.busy);
    try {
      const updated = await firstValueFrom(this.shop.schedule(piece.id, scheduledFor, piece.etag));
      this.item.set(updated);
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

  /** Every command but `schedule`, which carries a body and has {@link schedule} of its own (ADR 0036). */
  protected async run(command: Exclude<ShopCommand, 'schedule'>): Promise<void> {
    const piece = this.item();
    if (!piece || this.busy() || this.dirty()) return;

    this.errors.set({});
    this.formError.set(null);
    this.status.set({ kind: 'commanding', command });
    this.announce(COMMAND_LABELS[command].busy);
    try {
      const updated = await firstValueFrom(this.shop.command(piece.id, command, piece.etag));
      this.item.set(updated);
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

    if (problem?.code === SHOP_CODES.concurrencyConflict && this.item()) {
      try {
        const theirs = await firstValueFrom(this.shop.get(this.item()!.id));
        this.status.set({ kind: 'conflict', theirs });
        this.announce(
          'Someone else saved this piece while you were editing. Compare the two versions.',
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
      problem?.code === SHOP_CODES.concurrencyConflict ||
      problem?.code === SHOP_CODES.transitionInvalid;

    if (stale && this.item()) {
      try {
        const latest = await firstValueFrom(this.shop.get(this.item()!.id));
        this.item.set(latest);
        this.draft.set(draftOf(latest));
      } catch {
        // The message below still says what happened.
      }
    }

    this.status.set({ kind: 'ready' });
    this.showErrors(
      fieldErrorsOf(problem),
      problem?.code === SHOP_CODES.concurrencyConflict
        ? 'Someone else changed this piece a moment ago. Here is the latest version; check it and try again.'
        : messageOf(problem, 'That didn’t work. Try again.'),
    );
  }

  private showErrors(errors: FieldErrors, message: string | null): void {
    this.errors.set(errors);
    this.formError.set(message);
    this.announce(message ?? 'Some fields need attention.');
    // The first invalid field takes focus, so its message is read; the description's is the rich text's own.
    afterNextRender(
      () => {
        const invalid = this.host.nativeElement.querySelector<HTMLElement>(
          'input[aria-invalid="true"], textarea[aria-invalid="true"]',
        );
        if (invalid) {
          invalid.focus();
          return;
        }

        if (errors.description) {
          this.descriptionEditor()?.focus();
          return;
        }

        // The refusals with no control of their own: the gallery's and the categories'. Focus goes to what the
        // person would use to fix each.
        const selector = errors.imageMediaIds
          ? 'cms-gallery-editor [data-action="add"], cms-gallery-editor [data-action="remove"]'
          : errors.categories || errors.oneOfAKind
            ? 'input[type="checkbox"]'
            : null;
        if (selector) this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus();
      },
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

function money(cents: number, currency: string): string {
  return new Intl.NumberFormat('en-US', { style: 'currency', currency }).format(cents / 100);
}

function draftOf(piece: ShopItem): Draft {
  return {
    title: piece.title,
    slug: piece.slug,
    slugEdited: true,
    description: piece.description ?? '',
    artist: piece.artist
      ? { id: piece.artist.id, name: piece.artist.name, status: piece.artist.status }
      : null,
    categories: [...piece.categories],
    images: [...piece.imageMediaIds],
    sku: piece.sku,
    oneOfAKind: piece.oneOfAKind,
    price: piece.startingPriceCents === null ? '' : centsToPrice(piece.startingPriceCents),
    quantity: piece.startingStockQuantity === null ? '' : String(piece.startingStockQuantity),
  };
}

function createOf(draft: Draft): ShopItemCreate {
  const cents = priceToCents(draft.price);
  const quantity = Number(draft.quantity);
  return {
    title: draft.title.trim(),
    ...(draft.slug ? { slug: draft.slug } : {}),
    ...(draft.description ? { description: draft.description } : {}),
    ...(draft.artist ? { artistId: draft.artist.id } : {}),
    ...(draft.categories.length > 0 ? { categories: draft.categories } : {}),
    ...(draft.images.length > 0 ? { imageMediaIds: draft.images } : {}),
    ...(draft.sku.trim() ? { sku: normalizeSku(draft.sku) } : {}),
    oneOfAKind: draft.oneOfAKind,
    ...(cents === null ? {} : { startingPriceCents: cents }),

    // One of a kind is a count of one, which the API sets itself: sending anything else is refused (ADR 0038).
    ...(draft.oneOfAKind || !Number.isSafeInteger(quantity) ? {} : { startingStockQuantity: quantity }),
  };
}

/**
 * Only what changed; an emptied description is sent as "" so the API clears it. The price, the stock on hand and
 * sold-out are never here: they are Square's record (ADR 0032).
 */
function changesOf(piece: ShopItem, draft: Draft): ShopItemUpdate {
  const title = draft.title.trim();
  const cents = priceToCents(draft.price);
  const quantity = draft.quantity.trim() === '' ? null : Number(draft.quantity);
  const sku = draft.sku.trim() === '' ? piece.sku : normalizeSku(draft.sku);

  return {
    ...(title !== piece.title ? { title } : {}),
    ...(draft.slug !== piece.slug ? { slug: draft.slug } : {}),
    ...(draft.description !== (piece.description ?? '') ? { description: draft.description } : {}),
    ...(sku !== piece.sku ? { sku } : {}),
    ...(draft.artist?.id !== piece.artist?.id
      ? draft.artist === null
        ? { removeArtist: true as const }
        : { artistId: draft.artist.id }
      : {}),
    ...(!sameSet(draft.categories, piece.categories) ? { categories: draft.categories } : {}),
    ...(!sameOrder(draft.images, piece.imageMediaIds) ? { imageMediaIds: draft.images } : {}),
    ...(piece.startingStockEditable && draft.oneOfAKind !== piece.oneOfAKind
      ? { oneOfAKind: draft.oneOfAKind }
      : {}),
    ...(piece.startingPriceEditable && cents !== piece.startingPriceCents
      ? cents === null
        ? { removeStartingPrice: true as const }
        : { startingPriceCents: cents }
      : {}),

    // A one-of-a-kind count is the API's to set, so it is never sent for one.
    ...(piece.startingStockEditable &&
    !draft.oneOfAKind &&
    quantity !== null &&
    Number.isSafeInteger(quantity) &&
    quantity !== piece.startingStockQuantity
      ? { startingStockQuantity: quantity }
      : {}),
  };
}

function sameSet(a: readonly string[], b: readonly string[]): boolean {
  return a.length === b.length && [...a].sort().join('|') === [...b].sort().join('|');
}

function sameOrder(a: readonly string[], b: readonly string[]): boolean {
  return a.length === b.length && a.every((value, index) => value === b[index]);
}

function localErrors(draft: Draft): FieldErrors {
  const errors: FieldErrors = {};
  if (draft.title.trim() === '') errors.title = 'Enter the piece’s title.';
  if (!isSlug(draft.slug)) {
    errors.slug = draft.slug
      ? 'Use lower-case letters, digits and single hyphens (for example copper-cuff).'
      : 'Enter a web address, for example copper-cuff.';
  }
  if (draft.sku.trim() !== '' && !isSku(normalizeSku(draft.sku))) {
    errors.sku = 'A SKU is letters, digits, hyphens and dots, for example ART-17.';
  }
  if (draft.price.trim() !== '' && priceToCents(draft.price) === null) {
    errors.startingPriceCents = 'Enter the price as a number, for example 110 or 52.50.';
  }
  if (!draft.oneOfAKind) {
    const quantity = Number(draft.quantity);
    if (draft.quantity.trim() === '' || !Number.isSafeInteger(quantity) || quantity < 1) {
      errors.startingStockQuantity = 'Enter how many there are, as a whole number from 1.';
    }
  }
  return errors;
}

/** Cents as the price field shows them: whole currency units, with cents only when there are any. */
function centsToPrice(cents: number): string {
  return cents % 100 === 0 ? String(cents / 100) : (cents / 100).toFixed(2);
}

/** The price field as integer cents (api-contract.md), or `null` when it is empty or not a number. */
function priceToCents(price: string): number | null {
  const trimmed = price.trim();
  if (trimmed === '') return null;
  if (!/^\d+(?:\.\d{1,2})?$/.test(trimmed)) return null;
  return Math.round(Number(trimmed) * 100);
}

function problemOf(error: unknown): ShopProblem | null {
  return error instanceof HttpErrorResponse ? decodeShopProblem(error.error, error.status) : null;
}

function fieldErrorsOf(problem: ShopProblem | null): FieldErrors {
  const errors: FieldErrors = {};
  const keys: readonly FieldName[] = [
    'title',
    'slug',
    'description',
    'artistId',
    'categories',
    'imageMediaIds',
    'sku',
    'oneOfAKind',
    'startingPriceCents',
    'startingStockQuantity',
  ];
  for (const key of keys) {
    const message = problem?.fieldErrors[key]?.[0];
    if (message) errors[key] = message;
  }
  return errors;
}

function messageOf(problem: ShopProblem | null, fallback: string): string {
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
