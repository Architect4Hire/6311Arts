import { Injectable, inject } from '@angular/core';
import { Observable, map, of } from 'rxjs';
import {
  ArtClass,
  ArtistPage,
  CmsPage,
  Exhibition,
  Instructor,
  MembershipTier,
  Product,
} from '../core/models';
import { PreviewContext } from '../core/preview';
import { SiteApi } from './site-api.service';
import {
  toArtClasses,
  toArtistPage,
  toCmsPage,
  toExhibitions,
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

  /** The draft's runs among the published calendar, so the page's "more classes" still reads like the site. */
  override classes(): Observable<ArtClass[]> {
    const offering = this.previewed()?.classOffering;
    if (!offering) return super.classes();

    const runs = toArtClasses([offering]);
    return super
      .classes()
      .pipe(
        map((published) =>
          [...runs, ...published.filter((c) => c.slug !== offering.slug)].sort(
            (a, b) => Date.parse(a.start) - Date.parse(b.start),
          ),
        ),
      );
  }

  /** The exhibitions page is the show's only address, so a preview of one is that page showing it alone. */
  override exhibitions(): Observable<Exhibition[]> {
    const exhibition = this.previewed()?.exhibition;
    return exhibition ? of(toExhibitions([exhibition])) : super.exhibitions();
  }

  /** A tier has no page of its own either (ADR 0040), so the memberships page shows the previewed one alone. */
  override memberships(): Observable<MembershipTier[]> {
    const plan = this.previewed()?.membershipPlan;
    return plan ? of([toMembershipTier(plan)]) : super.memberships();
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
