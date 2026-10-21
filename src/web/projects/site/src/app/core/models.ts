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

export interface Exhibition {
  slug: string;
  title: string;
  start: string;
  end: string;
  summary: string;
  artists: string[];
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
