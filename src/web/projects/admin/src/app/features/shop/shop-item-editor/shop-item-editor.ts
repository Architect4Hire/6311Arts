import { ChangeDetectionStrategy, Component, computed, inject, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiControl, UiField } from '@6311/ui';
import {
  SHOP_CATEGORIES,
  SHOP_LIMITS,
  ShopItem,
  ShopItemCreate,
  ShopItemUpdate,
  categoryLabel,
  isSku,
  normalizeSku,
} from '../../../models/shop';
import { ShopService } from '../../../services/shop.service';
import { ContentEditor } from '../../../shared/content-editor';
import { EditorFrame } from '../../../shared/editor-frame/editor-frame';
import { ArtistPicker, PickedArtist } from '../../../shared/pickers/artist-picker/artist-picker';
import { PublishingPanel } from '../../../shared/publishing-panel/publishing-panel';
import { CompareRow, FieldErrors, Focusable, orDash, yesNo } from '../../../shared/record-editor';
import { RichTextEditor } from '../../../shared/rich-text-editor/rich-text-editor';
import { SaveActions } from '../../../shared/save-actions/save-actions';
import { centsToPrice, formatMoney, priceToCents, squarePrice, typed } from '../../../shared/money';
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

const FIELDS = [
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
] as const;
type Field = (typeof FIELDS)[number];

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

/**
 * One piece of artwork (`/admin/shop/new`, `/admin/shop/:id`): its copy, its artist, its categories, its
 * photographs and its SKU, and then publish, schedule, unpublish or (Admins) archive.
 *
 * **What the CMS owns and what Square does** (square.md, ADR 0032, 0038). The copy, the artist, the categories, the
 * photographs and the SKU are the CMS's, and so are the starting price and count — but only until Square reports a
 * price or stock. After that the figures shown are Square's, with the change-price and set-quantity commands
 * arriving in 5.4a and 5.4b, and a one-of-a-kind piece never gets the second of those.
 */
@Component({
  selector: 'cms-shop-item-editor',
  imports: [
    FormsModule,
    UiControl,
    UiField,
    ArtistPicker,
    GalleryEditor,
    RichTextEditor,
    EditorFrame,
    PublishingPanel,
    SaveActions,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shop-item-editor.html',
  styleUrl: './shop-item-editor.scss',
})
export class ShopItemEditor extends ContentEditor<
  ShopItem,
  Draft,
  ShopItemCreate,
  ShopItemUpdate,
  Field
> {
  private readonly descriptionEditor = viewChild(RichTextEditor);

  protected readonly limits = SHOP_LIMITS;
  protected readonly categories = SHOP_CATEGORIES;
  protected readonly item = this.record;
  protected readonly typed = typed;

  /** ADR 0032, 0038: the starting figures are ordinary fields only until Square holds the record. */
  protected readonly priceEditable = computed(() => this.item()?.startingPriceEditable ?? true);

  protected readonly priceHint = computed(
    () => `What it sells for, in ${this.item()?.currency ?? 'USD'}. Needed to publish.`,
  );

  protected readonly skuHint = computed(() => {
    const saved = this.item();
    if (!saved)
      return 'Left empty, the shop suggests one (for example ART-17). Unique across Studio.';
    return `Unique across Studio, class sessions included. Suggested as ART-${saved.itemNumber}.`;
  });

  constructor() {
    super({
      api: inject(ShopService),
      noun: 'piece',
      plural: 'artwork',
      listName: 'shop',
      route: '/shop',
      label: (piece) => piece.title,
      address: { path: '6311arts.com/shop/', from: 'title' },
      createdMessage: 'Piece created. Publish it when it’s ready.',
      createdState: 'shopItemCreated',
      empty: () => EMPTY,
      draftOf,
      createOf,
      changesOf,
      started: (draft) =>
        draft.title.trim() !== '' ||
        draft.description !== '' ||
        draft.artist !== null ||
        draft.categories.length > 0 ||
        draft.images.length > 0 ||
        draft.sku.trim() !== '' ||
        draft.price.trim() !== '',
      localErrors,
      fields: FIELDS,
      lifecycle: {
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
      },
    });
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

  protected squarePrice(piece: ShopItem): string {
    return squarePrice(piece.priceCents, piece.currency);
  }

  protected squareStock(piece: ShopItem): string {
    if (piece.soldOut) return 'Sold out in Square.';
    return piece.stockOnHand === null
      ? 'Square has not reported stock yet.'
      : `${piece.stockOnHand} in stock in Square.`;
  }

  protected override compare(theirs: ShopItem, draft: Draft): readonly CompareRow[] {
    return [
      { label: 'Title', mine: orDash(draft.title), theirs: theirs.title },
      { label: 'Web address', mine: `/shop/${draft.slug}`, theirs: `/shop/${theirs.slug}` },
      {
        label: 'Artist',
        mine: draft.artist?.name || 'Nobody',
        theirs: theirs.artist?.name || 'Nobody',
      },
      { label: 'SKU', mine: orDash(draft.sku), theirs: theirs.sku },
      { label: 'Categories', mine: labels(draft.categories), theirs: labels(theirs.categories) },
      {
        label: 'Photographs',
        mine: String(draft.images.length),
        theirs: String(theirs.imageMediaIds.length),
      },
      {
        label: 'Description',
        mine: `${draft.description.length} characters`,
        theirs: `${(theirs.description ?? '').length} characters`,
      },
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
          ]
        : []),
      ...(theirs.startingStockEditable
        ? [
            {
              label: 'One of a kind',
              mine: yesNo(draft.oneOfAKind),
              theirs: yesNo(theirs.oneOfAKind),
            },
            {
              label: 'Starting count',
              mine: draft.oneOfAKind ? 'One' : orDash(draft.quantity),
              theirs: theirs.oneOfAKind ? 'One' : String(theirs.startingStockQuantity ?? '—'),
            },
          ]
        : []),
    ];
  }

  protected override fieldsOf(keys: readonly string[]): readonly string[] {
    return keys.map((key) =>
      key === 'artist' ? 'artistId' : key === 'images' ? 'imageMediaIds' : key,
    );
  }

  /**
   * The description's error is the rich text's own. The refusals with no control of their own — the gallery's and
   * the categories' — send focus to what the person would use to fix each.
   */
  protected override focusFallback(errors: FieldErrors<Field>): Focusable | null | undefined {
    if (errors.description) return this.descriptionEditor();
    const selector = errors.imageMediaIds
      ? 'cms-gallery-editor [data-action="add"], cms-gallery-editor [data-action="remove"]'
      : errors.categories || errors.oneOfAKind
        ? 'input[type="checkbox"]'
        : null;
    return selector ? this.host.nativeElement.querySelector<HTMLElement>(selector) : null;
  }
}

function labels(categories: readonly string[]): string {
  return categories.length === 0 ? 'None' : categories.map(categoryLabel).join(', ');
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

function localErrors(draft: Draft): FieldErrors<Field> {
  const errors: FieldErrors<Field> = {};
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
