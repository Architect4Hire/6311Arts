import {
  ArtClass,
  Artist,
  ClassLevel,
  ClassType,
  CmsPage,
  Exhibition,
  Instructor,
  MembershipTier,
  Product,
  ProductPage,
} from '../core/models';
import {
  SiteArtistSummary,
  SiteClassSummary,
  SiteCredit,
  SiteExhibitionSummary,
  SiteInstructor,
  SiteMembershipPlan,
  SitePage,
  SiteShopItem,
  SiteShopItemSummary,
  SiteShopPage,
} from '../models/site';

/**
 * Pure mappings from the site read API's models to the shapes the pages render (ADR 0042's table, read backwards).
 * Nothing here fetches or decides what is shown; it only moves fields across.
 */

const CLASS_TYPES: readonly ClassType[] = [
  'Printmaking',
  'Drawing',
  'Painting',
  'Mixed media',
  'Talk',
  'Event',
  'Other',
];
const CLASS_LEVELS: readonly ClassLevel[] = ['Beginner', 'Intermediate', 'All levels'];

/**
 * An instant as the wall clock in `timeZone` reads it, with that zone's offset: `2026-11-10T18:00:00-06:00`. The API
 * sends UTC; the pages order a day's classes by the time part of the string (`start.slice(11)`) and file them under
 * its Chicago date, so they need local times, as the spike's data had.
 */
export function zonedIso(instant: string, timeZone: string): string {
  const at = new Date(instant);
  if (Number.isNaN(at.getTime())) return instant;
  const parts: Record<string, string> = {};
  for (const p of new Intl.DateTimeFormat('en-US', {
    timeZone,
    hourCycle: 'h23',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).formatToParts(at)) {
    parts[p.type] = p.value;
  }
  const wall = Date.UTC(
    +parts['year'],
    +parts['month'] - 1,
    +parts['day'],
    +parts['hour'],
    +parts['minute'],
    +parts['second'],
  );
  const offset = Math.round((wall - Math.floor(at.getTime() / 1000) * 1000) / 60_000);
  const abs = Math.abs(offset);
  const pad = (n: number) => String(n).padStart(2, '0');
  return (
    `${parts['year']}-${parts['month']}-${parts['day']}T${parts['hour']}:${parts['minute']}:${parts['second']}` +
    `${offset < 0 ? '-' : '+'}${pad(Math.floor(abs / 60))}:${pad(abs % 60)}`
  );
}

function classType(value: string | null): ClassType {
  return CLASS_TYPES.find((t) => t.toLowerCase() === value?.trim().toLowerCase()) ?? 'Other';
}

function classLevel(value: string | null): ClassLevel {
  return CLASS_LEVELS.find((l) => l.toLowerCase() === value?.trim().toLowerCase()) ?? 'All levels';
}

function credit(c: SiteCredit | null): Product['artist'] {
  return c ? { name: c.name, ...(c.slug ? { slug: c.slug } : {}) } : undefined;
}

const orUndefined = <T>(value: T | null): T | undefined => (value === null ? undefined : value);

/**
 * Every upcoming run of every class, soonest first: the spike listed one entry per dated offering, and a class taught
 * over two terms is two of those. Each carries its own run's instructor, price and seats.
 */
export function toArtClasses(list: readonly SiteClassSummary[]): ArtClass[] {
  return list
    .flatMap((c) =>
      c.runs.map((run): ArtClass => {
        const first = run.sessions[0] ?? { startsAt: run.startsAt, endsAt: run.endsAt };
        return {
          id: run.id,
          slug: c.slug,
          title: c.title,
          start: zonedIso(first.startsAt, run.displayTimeZone),
          end: zonedIso(first.endsAt, run.displayTimeZone),
          sessions: run.sessionCount,
          cadence: orUndefined(run.cadence),
          type: classType(c.classType),
          level: classLevel(c.level),
          price: orUndefined(run.priceCents),
          instructor: run.instructor?.name,
          image: c.image?.src,
          summary: c.summary ?? '',
          // Square's count once it has one; a sold-out flag before any count still reads as none left.
          spotsLeft: run.seatsRemaining ?? (run.soldOut ? 0 : undefined),
          capacity: run.capacity,
        };
      }),
    )
    .sort((a, b) => Date.parse(a.start) - Date.parse(b.start));
}

export function toInstructor(i: SiteInstructor): Instructor {
  return {
    slug: i.slug,
    name: i.name,
    medium: orUndefined(i.medium),
    bio: orUndefined(i.bio),
    image: i.portrait?.src,
    website: orUndefined(i.website),
    instagram: orUndefined(i.instagram),
    artistSlug: orUndefined(i.artistSlug),
  };
}

/** Newest opening first, as the spike sorted them. */
export function toExhibitions(list: readonly SiteExhibitionSummary[]): Exhibition[] {
  return list
    .map((e): Exhibition => ({
      slug: e.slug,
      title: e.title,
      start: e.startDate,
      end: e.endDate,
      summary: e.summary ?? '',
      artists: e.artists.map((a) => a.name),
      image: e.image?.src,
      reception: e.reception
        ? {
            start: zonedIso(e.reception.startsAt, e.displayTimeZone),
            end: zonedIso(e.reception.endsAt, e.displayTimeZone),
            note: orUndefined(e.reception.note),
          }
        : undefined,
    }))
    .sort((a, b) => b.start.localeCompare(a.start));
}

export function toArtist(a: SiteArtistSummary): Artist {
  return {
    slug: a.slug,
    name: a.name,
    medium: orUndefined(a.medium),
    productCount: a.workCount,
    image: a.portrait?.src,
  };
}

export function toProduct(p: SiteShopItemSummary | SiteShopItem): Product {
  const html = 'descriptionHtml' in p ? p.descriptionHtml : null;
  return {
    id: p.id,
    slug: p.slug,
    name: p.title,
    artist: credit(p.artist),
    price: orUndefined(p.priceCents),
    image: p.image?.src,
    categories: [...p.categories],
    inStock: !p.soldOut,
    description: html ? stripTags(html) || undefined : undefined,
  };
}

export function toProductPage(page: SiteShopPage): ProductPage {
  return { items: page.items.map(toProduct), total: page.totalCount, nextCursor: page.nextCursor };
}

export function toMembershipTier(m: SiteMembershipPlan): MembershipTier {
  return {
    id: m.id,
    slug: m.slug,
    name: m.name,
    price: orUndefined(m.priceCents),
    perks: [...m.perks],
    discount: m.discountPercent,
    featured: m.featured,
  };
}

export function toCmsPage(p: SitePage): CmsPage {
  return { slug: p.slug, title: p.title, html: p.bodyHtml ?? '' };
}

const ENTITIES: Record<string, string> = {
  '&amp;': '&',
  '&lt;': '<',
  '&gt;': '>',
  '&quot;': '"',
  '&#39;': '’',
  '&nbsp;': ' ',
};

/**
 * A piece's description as one line of text: the page shows it as a lead and uses it for the meta description,
 * as the spike did with WooCommerce's. The HTML is the sanitizer's (ADR 0034, 0045), so tags are all there is.
 */
export function stripTags(html: string): string {
  return html
    .replace(/<[^>]+>/g, ' ')
    .replace(/&[#a-z0-9]+;/gi, (m) => ENTITIES[m] ?? m)
    .replace(/\s+/g, ' ')
    .trim();
}
