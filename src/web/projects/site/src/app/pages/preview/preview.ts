import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  input,
} from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { map, startWith, switchMap, tap } from 'rxjs';
import { noStoreNoIndex } from '../../core/no-store';
import { PreviewContext } from '../../core/preview';
import { PreviewSiteApi } from '../../services/preview-site-api.service';
import { SiteApi } from '../../services/site-api.service';
import { ArtistDetail } from '../artist-detail/artist-detail';
import { ClassDetail } from '../class-detail/class-detail';
import { CmsPage } from '../cms-page/cms-page';
import { Exhibitions } from '../exhibitions/exhibitions';
import { InstructorDetail } from '../instructor-detail/instructor-detail';
import { Memberships } from '../memberships/memberships';
import { NotFound } from '../not-found/not-found';
import { ProductDetail } from '../product-detail/product-detail';

/**
 * `/preview/{token}`: one unpublished record as the site will show it, for whoever holds the link (ADR 0036, 0063).
 *
 * The token is the whole credential — the person following it has no session — so the page reads
 * `/api/v1/site/preview/{token}` and renders **the site's own page component** over a `SiteApi` seeded with the
 * answer. Nothing is duplicated: what staff see here is the page visitors will see, with a bar across the top
 * saying it is a draft. A token that has expired, been tampered with, or names a record since deleted is one
 * answer and one page: the site's 404.
 *
 * Exhibitions and membership tiers have no address of their own, so those render as the page that shows them
 * (`/exhibitions`, `/memberships`) carrying the one record.
 *
 * The response is `no-store` with `X-Robots-Tag: noindex, nofollow`, the head is `noindex, nofollow` with no
 * canonical, and `/preview/` is disallowed in `robots.txt` — a preview never reaches an index or a shared cache.
 */
@Component({
  selector: 'app-preview',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ArtistDetail,
    ClassDetail,
    CmsPage,
    Exhibitions,
    InstructorDetail,
    Memberships,
    NotFound,
    ProductDetail,
  ],
  templateUrl: './preview.html',
  styleUrl: './preview.scss',
  // Every page rendered below reads the draft through this, and nothing outside this route provides it.
  providers: [{ provide: SiteApi, useClass: PreviewSiteApi }],
})
export class Preview {
  /** The route's `:token`, bound by the router. It is a credential: it is never stored, logged or linked to. */
  readonly token = input.required<string>();

  private readonly api = inject(SiteApi);
  private readonly context = inject(PreviewContext);

  /** `undefined` while the token is being read; `{ preview: undefined }` once it turns out to be unusable. */
  private readonly data = toSignal(
    toObservable(this.token).pipe(
      // A second link opened in the same tab starts the page over: the draft before it goes before the new token
      // is read, so nothing of the one is ever on screen under the address of the other.
      tap(() => this.context.leave()),
      switchMap((token) =>
        this.api.preview(token).pipe(
          // Set before the template renders the page below, which reads the record through `PreviewSiteApi`.
          tap((preview) => preview && this.context.enter(preview)),
          map((preview) => ({ preview })),
          startWith(undefined),
        ),
      ),
    ),
  );

  protected readonly loaded = computed(() => this.data() !== undefined);
  protected readonly preview = computed(() => this.data()?.preview);

  protected readonly artist = computed(() => this.preview()?.artist ?? null);
  protected readonly instructor = computed(() => this.preview()?.instructor ?? null);
  protected readonly classOffering = computed(() => this.preview()?.classOffering ?? null);
  protected readonly shopItem = computed(() => this.preview()?.shopItem ?? null);
  protected readonly page = computed(() => this.preview()?.page ?? null);
  protected readonly exhibition = computed(() => this.preview()?.exhibition ?? null);
  protected readonly membershipPlan = computed(() => this.preview()?.membershipPlan ?? null);

  /** What the bar says this is: "Draft artist", "Scheduled exhibition". */
  protected readonly describes = computed(() => {
    const preview = this.preview();
    return preview ? `${preview.status} ${TYPE_NAMES[preview.entityType]}` : '';
  });

  /**
   * How the bar introduces the address. A record with a page of its own will *have* that address once it is
   * published; an exhibition and a membership tier never do — they are shown on a page they share (ADR 0040) —
   * so the bar does not promise them one.
   */
  protected readonly addresses = computed(() => {
    const type = this.preview()?.entityType;
    return type === 'exhibition' || type === 'membership-plan'
      ? 'It is shown at'
      : 'Its address once published is';
  });

  constructor() {
    noStoreNoIndex();
    // A preview is one navigation's state: it goes when the page does, so the next page is an ordinary one.
    inject(DestroyRef).onDestroy(() => this.context.leave());
  }
}

/** The content types in staff's words, as the CMS names them. */
const TYPE_NAMES = {
  artist: 'artist',
  instructor: 'instructor',
  exhibition: 'exhibition',
  page: 'page',
  'class-offering': 'class',
  'shop-item': 'piece of artwork',
  'membership-plan': 'membership tier',
} as const;
