/** Normalized shapes the UI renders. The WordPress service maps API responses into these. */

export interface Product {
  id: number;
  slug: string;
  name: string;
  artist?: { name: string; slug: string };
  /** Price in cents, as the WooCommerce Store API returns it. */
  price: number;
  image?: string;
  categories: string[];
  inStock: boolean;
  description?: string;
}

export type ClassType = 'Printmaking' | 'Drawing' | 'Painting' | 'Mixed media' | 'Talk' | 'Event' | 'Other';
export type ClassLevel = 'Beginner' | 'Intermediate' | 'All levels';

export interface ArtClass {
  id: number;
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
  price: number;
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

export interface Artist {
  slug: string;
  name: string;
  medium?: string;
  productCount?: number;
  image?: string;
}

export interface MembershipTier {
  id: number;
  slug: string;
  name: string;
  price: number;
  perks: string[];
  discount: number;
  featured?: boolean;
}

export interface CmsPage {
  slug: string;
  title: string;
  html: string;
}
