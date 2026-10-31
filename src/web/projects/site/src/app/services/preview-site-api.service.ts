import { Injectable, inject } from '@angular/core';
import { Observable, map, of } from 'rxjs';
import {
  ArtistPage,
  ClassPage,
  CmsPage,
  Exhibition,
  Instructor,
  MembershipTier,
  Product,
} from '../core/models';
import { PreviewContext } from '../core/preview';
import { SiteApi } from './site-api.service';
import {
  toArtistPage,
  toClassPage,
  toCmsPage,
  toExhibition,
  toInstructor,
  toMembershipTier,
  toProduct,
} from './site-mappers';

/**
 * The site read API with one draft seeded into it, provided by the preview page in place of `SiteApi` (ADR 0063).
 *
 * The site's pages read published records by slug, so a draft has nothing for them to find. Rather than give
 * previews a second rendering of every content type, the preview page renders the real page component over this,
 * which answers the one read that asks for the previewed record and passes everything else to the real API — so
 * the surroundings (the shop's other work, the classes calendar, the membership discount) are the live site's.
 *
 * Only reads a previewable type can reach are overridden, and each falls through when the preview is of some
 * other type — and the reads that take a slug fall through unless it is the draft's own, so one seeded record can
 * never answer for another address. Nothing here writes, and nothing outside the preview page provides it.
 */
@Injectable()
export class PreviewSiteApi extends SiteApi {
  private readonly previewed = inject(PreviewContext).record;

  /** Their work is the shop's, and the shop lists published pieces only: a draft artist shows none yet. */
  override artist(slug: string): Observable<ArtistPage | undefined> {
    const artist = this.previewed()?.artist;
    if (artist?.slug !== slug) return super.artist(slug);

    const page = toArtistPage({ artist, works: { items: [], nextCursor: null } });
    return super
      .products({ artist: artist.slug, limit: 12 })
      .pipe(map((works) => ({ ...page, works: works.items })));
  }

  /** The roster the instructor's page looks itself up in: the draft alone, since that is who is being previewed. */
  override instructors(): Observable<Instructor[]> {
    const instructor = this.previewed()?.instructor;
    return instructor ? of([toInstructor(instructor)]) : super.instructors();
  }

  /**
   * The class's own page, which a draft has no published row behind (ADR 0065).
   *
   * `classes()` is left to the real API, so the calendar below the page is the live one.
   */
  override classPage(slug: string): Observable<ClassPage | undefined | null> {
    const offering = this.previewed()?.classOffering;
    return offering?.slug === slug ? of(toClassPage(offering)) : super.classPage(slug);
  }

  /** The show's own page, which a draft has no published row behind (ADR 0064). */
  override exhibition(slug: string): Observable<Exhibition | undefined | null> {
    const exhibition = this.previewed()?.exhibition;
    return exhibition?.slug === slug ? of(toExhibition(exhibition)) : super.exhibition(slug);
  }

  /**
   * A tier has no page of its own (ADR 0040): every one is a column on the memberships page, and how that page
   * reads — the column count, which tier is featured, the "join from" price — is the published tiers plus this
   * one. So the draft joins them rather than standing alone, replacing its published self when it has one.
   *
   * It joins at the end unless it does: the site's tier model carries no display order (the API's order *is* the
   * list), so a new tier's real position among them is not knowable here.
   */
  override memberships(): Observable<MembershipTier[]> {
    const plan = this.previewed()?.membershipPlan;
    if (!plan) return super.memberships();

    const draft = toMembershipTier(plan);
    return super
      .memberships()
      .pipe(
        map((published) =>
          published.some((tier) => tier.slug === draft.slug)
            ? published.map((tier) => (tier.slug === draft.slug ? draft : tier))
            : [...published, draft],
        ),
      );
  }

  override product(slug: string): Observable<Product | undefined> {
    const item = this.previewed()?.shopItem;
    return item?.slug === slug ? of(toProduct(item)) : super.product(slug);
  }

  override page(slug: string): Observable<CmsPage | undefined> {
    const page = this.previewed()?.page;
    return page?.slug === slug ? of(toCmsPage(page)) : super.page(slug);
  }
}
