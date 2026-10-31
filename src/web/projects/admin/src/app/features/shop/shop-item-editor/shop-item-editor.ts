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
import { STATUS_CHIPS, slugFromName } from '../../../models/content';
import {
  SHOP_CATEGORIES,
  SHOP_CODES,
  SHOP_LIMITS,
  ShopItem,
  ShopItemCreate,
  ShopItemUpdate,
  ShopProblem,
  categoryLabel,
  isSku,
  normalizeSku,
} from '../../../models/shop';
import { ShopService } from '../../../services/shop.service';
import { ArtistPicker, PickedArtist } from '../../../shared/pickers/artist-picker/artist-picker';
import { RichTextEditor } from '../../../shared/rich-text-editor/rich-text-editor';
import { GalleryEditor } from '../gallery-editor/gallery-editor';

interface Draft {
  readonly title: string;
  readonly slug: string;
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

const LIFECYCLE: LifecycleCopy = {
  noun: 'piece',
  keepLabel: 'Keep it',
  inSquare: true,
  previewType: 'shop-item',
  unpublishMessage:
    'It goes back to being a draft and leaves the shop. Its web address stays the same, so publishing again restores it.',
  archiveMessage:
    'An archived piece is retired from the shop, and archiving can’t be undone from Studio. The archive is recorded under your name.',
  deleteText:
    'The piece leaves the site and the list, and its web address is free again. An Admin can restore it from Deleted, as a draft.',
  published: 'Published: in the shop. Edits show there within a minute.',
  archived: 'Archived: retired from the shop. Archiving can’t be undone here.',
  redrafted: 'A draft again, after being published. Its web address stays as it is.',
  draft:
    'A draft: only staff can see it. Publishing needs a description, a price and a described photograph.',
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
    RichTextEditor,
    PublishingPanel,
    UiButton,
    UiControl,
    UiField,
    UiNotice,
    UiStatusChip,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shop-item-editor.html',
  styleUrl: './shop-item-editor.scss',
})
export class ShopItemEditor implements HasUnsavedChanges {
  /** The route's `:id`; absent on `/shop/new`. */
  readonly id = input<string>();

  private readonly shop = inject(ShopService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly conflictBox = viewChild<ElementRef<HTMLElement>>('conflictBox');
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
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

  /** Publish, unpublish, schedule, archive and delete, shown by `<cms-publishing-panel>`. */
  protected readonly lifecycle: ContentLifecycle<ShopItem> = new ContentLifecycle({
    copy: LIFECYCLE,
    api: this.shop,
    record: () => this.item(),
    label: (saved) => saved.title,
    dirty: () => this.dirty(),
    busy: () => this.busy(),
    replace: (saved) => {
      this.item.set(saved);
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
      return this.router.navigate(['/shop']);
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
    if (!saved)
      return 'Left empty, the shop suggests one (for example ART-17). Unique across Studio.';
    return `Unique across Studio, class sessions included. Suggested as ART-${saved.itemNumber}.`;
  });

  /** Made from the title on create and never typed (the field only shows it), so the address never moves. */
  protected readonly slugHint = computed(() => {
    const slug = this.draft().slug;
    return this.item()
      ? `6311arts.com/shop/${slug}`
      : `6311arts.com/shop/${slug || '…'} — made from the title. If another piece has it, a number is added.`;
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
    // A new piece's address follows its title; staff never type one, and the API numbers a taken one.
    const followsTitle = !this.item();
    this.patch({ title, ...(followsTitle ? { slug: slugFromName(title) } : {}) });
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
    ...(draft.description ? { description: draft.description } : {}),
    ...(draft.artist ? { artistId: draft.artist.id } : {}),
    ...(draft.categories.length > 0 ? { categories: draft.categories } : {}),
    ...(draft.images.length > 0 ? { imageMediaIds: draft.images } : {}),
    ...(draft.sku.trim() ? { sku: normalizeSku(draft.sku) } : {}),
    oneOfAKind: draft.oneOfAKind,
    ...(cents === null ? {} : { startingPriceCents: cents }),

    // One of a kind is a count of one, which the API sets itself: sending anything else is refused (ADR 0038).
    ...(draft.oneOfAKind || !Number.isSafeInteger(quantity)
      ? {}
      : { startingStockQuantity: quantity }),
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
