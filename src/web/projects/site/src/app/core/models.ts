/**
 * Normalized shapes the UI renders. `SiteApi` maps the site read API's models (`../models/site.ts`) into these
 * (`site-mappers.ts`), so the pages kept the spike's shapes when WordPress went away.
 */

export interface Product {
  id: string;
  slug: string;
  name: string;
  /** Who made it; `slug` is absent when they have no published page, so the name shows without a link. */
  artist?: { name: string; slug?: string };
  /** Price in cents: Square's record, absent until Square has priced it, and until then it isn't for sale. */
  price?: number;
  image?: string;
  categories: string[];
  inStock: boolean;
  description?: string;
}

export type ClassType =
  'Printmaking' | 'Drawing' | 'Painting' | 'Mixed media' | 'Talk' | 'Event' | 'Other';
export type ClassLevel = 'Beginner' | 'Intermediate' | 'All levels';

/** One run of a class: the dates a seat buys. A class taught over two terms is two of these with one slug. */
export interface ArtClass {
  id: string;
  slug: string;
  title: string;
  /** ISO date-time of the first session (local Chicago time). */
  start: string;
  /** ISO date-time the first session ends. */
  end: string;
  sessions: number;
  /** Human cadence, e.g. "Saturdays". */
  cadence?: string;
  type: ClassType;
  level: ClassLevel;
  /** Square's record of the price, absent until Square has reported one. */
  price?: number;
  instructor?: string;
  image?: string;
  summary: string;
  spotsLeft?: number;
  capacity?: number;
}

/**
 * One class as its own page shows it (ADR 0065): the copy staff wrote, and every run still to come — which is
 * empty while a class is being written and again once it is over. The page is the class; the runs are its dates.
 */
export interface ClassPage {
  slug: string;
  title: string;
  summary: string;
  /** The CMS's "More details", sanitized HTML (ADR 0045). */
  body?: string;
  type: ClassType;
  level: ClassLevel;
  image?: string;
  /** Soonest first. One entry per run, the same shape the calendar lists. */
  runs: ArtClass[];
}

export interface Exhibition {
  slug: string;
  title: string;
  start: string;
  end: string;
  summary: string;
  /** The credited artists' names, for the card and the list. */
  artists: string[];
  /** The same credits with their pages, where they have one: a credit names an unpublished artist without a link. */
  credits?: { name: string; slug?: string }[];
  /** The show's own write-up, a paragraph per blank line. Only the show's page reads it. */
  body?: string[];
  image?: string;
  reception?: { start: string; end: string; note?: string };
}

export interface Instructor {
  slug: string;
  name: string;
  /** What they teach here, e.g. "Printmaking". */
  medium?: string;
  bio?: string;
  image?: string;
  neighborhood?: string;
  website?: string;
  /** Handle without the @. */
  instagram?: string;
  /** Set when the instructor also sells in the shop, so the page can link to their work. */
  artistSlug?: string;
}

export interface Artist {
  slug: string;
  name: string;
  medium?: string;
  productCount?: number;
  image?: string;
  /** The bio, one string per paragraph; empty when staff have not written one. */
  bio?: string[];
  /** Whether they currently show at the gallery. */
  active?: boolean;
}

/** One artist's page: who they are, and the first page of their work in the shop. */
export interface ArtistPage {
  artist: Artist;
  works: Product[];
  /** Where the rest of their work resumes in the shop list, or `null` when this is all of it. */
  nextCursor: string | null;
}

export interface MembershipTier {
  id: string;
  slug: string;
  name: string;
  /** Square's record of the yearly price, absent until Square has reported one. */
  price?: number;
  perks: string[];
  discount: number;
  featured?: boolean;
}

export interface CmsPage {
  slug: string;
  title: string;
  html: string;
}

/** One page of the shop: the works on it, how many the filters match in all, and where the next page starts. */
export interface ProductPage {
  items: Product[];
  total: number;
  nextCursor: string | null;
}
