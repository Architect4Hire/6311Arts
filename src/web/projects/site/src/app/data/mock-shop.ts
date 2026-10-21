import { Artist, MembershipTier, Product } from '../core/models';

/**
 * Sample of the shop and artist list copied from 6311arts.com on 2026-10-18, re-pulled from the WooCommerce
 * Store API so every id, price and photo matches the live catalogue. Used when the API can't be reached.
 *
 * Every item carries its real colour photograph from the WordPress uploads folder — the fallback shop is meant
 * to look like the shop, not like a grid of empty tiles.
 */
const up = (path: string, size = 'fit=800%2C1000') => `https://i0.wp.com/6311arts.com/wp-content/uploads/${path}?${size}&ssl=1`;

const a = (name: string) => ({ name, slug: slugify(name) });

export function slugify(s: string): string {
  return s
    .toLowerCase()
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/(^-|-$)/g, '');
}

/** WooCommerce's numeric ids as copied; the site's ids are strings, so they are converted on export. */
const PRODUCTS: (Omit<Product, 'id'> & { id: number })[] = [
  { id: 792, slug: 'ammonite-fossil-and-variscite-oxidized-silver-necklace-22chain-carrie-haase', name: 'Ammonite Fossil & Variscite Necklace', artist: a('Carrie Haase'), price: 52500, image: up('2026/09/Ammonite-Fossil-and-Variscite-Oxidized-Silver-Necklace-22chain-necklace.jpeg'), categories: ['jewelry'], inStock: true },
  { id: 734, slug: 'large-branch-handle-bowl-susan-volk', name: 'Large Branch Handle Bowl', artist: a('Susan Volk'), price: 27500, image: up('2026/09/Large-branch-handle-bowl-Susan-Volk.jpeg'), categories: ['ceramics'], inStock: true },
  { id: 709, slug: 'the-recovery-series-pt-2-6-stephen-bell', name: 'The Recovery Series (Pt. 2) #6', artist: a('Stephen Bell'), price: 12500, image: up('2026/09/The-Recovery-Series-Pt.-2-6-decorative-tree-trunk.jpeg'), categories: ['sculpture'], inStock: true },
  { id: 788, slug: 'australian-opal-silver-ring-carrie-haase', name: 'Australian Opal Silver Ring', artist: a('Carrie Haase'), price: 26500, image: up('2026/09/Australian-Opal-Silver-Ring-jewelry.jpeg'), categories: ['jewelry'], inStock: true },
  { id: 703, slug: 'mushrooms-in-fall-audrey-avril-2', name: 'Mushrooms in Fall (print)', artist: a('Audrey Avril'), price: 1000, image: up('2026/09/Mushrooms-In-Fall-print-1.jpeg'), categories: ['prints'], inStock: true },
  { id: 790, slug: 'peekaboo-sterling-oval-ring-size-9-carrie-haase', name: 'Peekaboo Sterling Oval Ring', artist: a('Carrie Haase'), price: 30000, image: up('2026/09/Peekaboo-Sterling-Oval-Ring-size-9-jewelry.jpeg'), categories: ['jewelry'], inStock: true },
  { id: 2071, slug: 'boystown-aaron-wooten', name: 'Boystown', artist: a('Aaron Wooten'), price: 40000, image: up('2026/09/Boystown-collage.jpeg'), categories: ['painting'], inStock: true },
  { id: 699, slug: 'grandma-turn-me-into-a-newt-next-audrey-avril', name: 'Grandma (Turn Me Into a Newt Next)', artist: a('Audrey Avril'), price: 800, image: up('2026/09/Grandma-Turn-Me-Into-A-Newt-Next-print.jpeg'), categories: ['prints'], inStock: true },
  { id: 4527, slug: 'copper-cuff-jewelry', name: 'Copper Cuff', artist: a('Carrie Haase'), price: 11000, image: up('2026/09/Copper-Cuff-jewelry.jpeg'), categories: ['jewelry'], inStock: true },
  { id: 705, slug: 'anabelles-first-summon-audrey-avril', name: 'Anabelle’s First Summon (print)', artist: a('Audrey Avril'), price: 1000, image: up('2026/09/Anabelles-First-Summon-print.jpeg'), categories: ['prints'], inStock: true },
  { id: 4537, slug: 'lodolite-pendant-jewelry', name: 'Lodolite Pendant', artist: a('Carrie Haase'), price: 35000, image: up('2026/09/Lodolite-Pendant-jewelry.jpeg'), categories: ['jewelry'], inStock: true },
  { id: 2018, slug: 'piloto-book-piloto', name: 'Piloto (sculpture book)', artist: a('Piloto'), price: 3500, image: up('2026/09/Piloto-sculpture-book.jpeg'), categories: ['books-magazines'], inStock: true },
  { id: 701, slug: 'the-guide-audrey-avril-2', name: 'The Guide (print)', artist: a('Audrey Avril'), price: 800, image: up('2026/09/The-Guide-print-1.jpeg'), categories: ['prints'], inStock: true },
  { id: 4539, slug: 'serpentine-pendant-jewelry', name: 'Serpentine Pendant', artist: a('Carrie Haase'), price: 23500, image: up('2026/09/Serpentine-Pendant-jewelry.jpeg'), categories: ['jewelry'], inStock: true },
  { id: 784, slug: 'minimalist-sterling-silver-hexagon-studs-carrie-haase', name: 'Sterling Hexagon Studs', artist: a('Carrie Haase'), price: 13500, image: up('2026/09/Minimalist-Sterling-Silver-Hexagon-Studs-earrings.jpeg'), categories: ['jewelry'], inStock: true },
  { id: 2046, slug: 'chicago-collage-magazine-vol-1', name: 'Chicago Collage Magazine Vol. 1', price: 2000, image: up('2026/09/Chicago-Collage-Magazine-Vol-1.jpeg'), categories: ['books-magazines'], inStock: true },
  { id: 4531, slug: 'iron-buffalo-ring-jewelry', name: 'Iron Buffalo Ring', artist: a('Carrie Haase'), price: 24500, image: up('2026/09/Iron-Buffalo-Ring-jewelry.jpeg'), categories: ['jewelry'], inStock: true },
  { id: 4529, slug: 'petrified-palmwood-earrings-carrie-haase', name: 'Petrified Palmwood Earrings', artist: a('Carrie Haase'), price: 29500, image: up('2026/09/Petrified-Palmwood-Earrings-Carrie-Haase.jpeg'), categories: ['jewelry'], inStock: true },
];

export const MOCK_PRODUCTS: Product[] = PRODUCTS.map((p) => ({ ...p, id: String(p.id) }));

const NAMES = [
  'Mike Sebahar', 'Candice Johnson', 'Socorro Mucino', 'Micki Croisant', 'Mike Winchester', 'Nolan Code', 'Susan Volk',
  'Jesy Grose', 'Harry Sudman', 'Wendy Love', 'Brian Eaton', 'Todd Lewis', 'Audrey Avril', 'Joseph Frumkin',
  "Marco Da'Vila", 'Carrie Haase', 'Hedda Lubin', 'Kevin Johnson', 'Justin Matthew', 'Eddie Pedroza', 'Aaron Becker',
  'Ralph Skunkie Davis', 'Annette Baksinskas', 'Madeline Usher', 'Xavier Wynn', 'Roslyn Broder', 'Carol Hammerman',
  'Paul Schultz', 'Michael Johnson', 'Stephen Bell', 'Alejandro Figueroa', 'Hector Hernandez', 'Katherine Knapp',
  'Aaron Wooten', 'Anoush Bargamian', 'Jenna Summa', 'Keith Sklar', 'Jeanne Reilly', 'Anne Terpstra',
  'Jayne Lilienfeld', 'Adrian Zavala', 'Mary Laskey', 'Eddwin Meyers', 'Alejandra Guerrero', 'Jeffrey Brooks',
  'Piloto', 'Dan Steinke', 'Jeremy Humbert', 'Jacob Vega', 'Lisa Barcy', 'Jacobo',
];

/** Counts are from the WooCommerce artist categories where known. */
const COUNTS: Record<string, [number, string?]> = {
  'Candice Johnson': [54, 'Drawing & mixed media'],
  'Harry Sudman': [24, 'Painting'],
  'Brian Eaton': [17, 'Painting'],
  'Carrie Haase': [17, 'Jewelry'],
  'Audrey Avril': [14, 'Illustration & prints'],
  'Eddie Pedroza': [14, 'Photography'],
  'Hedda Lubin': [14],
  'Aaron Becker': [13],
  'Annette Baksinskas': [11],
  'Alejandro Figueroa': [7],
  'Hector Hernandez': [7, 'Portraiture'],
  'Carol Hammerman': [6],
  'Anoush Bargamian': [6],
  'Jayne Lilienfeld': [6],
  'Anne Terpstra': [5],
  'Aaron Wooten': [4, 'Painting'],
  'Adrian Zavala': [3],
  'Eddwin Meyers': [3],
  'Alejandra Guerrero': [2],
  'Stephen Bell': [0, 'Sculpture'],
  'Susan Volk': [0, 'Ceramics'],
  Piloto: [0, 'Sculpture'],
};

export const MOCK_ARTISTS: Artist[] = NAMES.map((name) => ({
  name,
  slug: slugify(name),
  productCount: COUNTS[name]?.[0] || undefined,
  medium: COUNTS[name]?.[1],
})).sort((x, y) => x.name.localeCompare(y.name));

const TIERS: (Omit<MembershipTier, 'id'> & { id: number })[] = [
  { id: 0, slug: 'gray-membership', name: 'Gray', price: 10000, discount: 5, perks: ['5% off every class & workshop', 'Annual membership party', '6311arts tote'] },
  {
    id: 0,
    slug: 'black-membership',
    name: 'Black',
    price: 30000,
    discount: 10,
    featured: true,
    perks: ['10% off every class & workshop', 'Annual membership party', '6311arts t-shirt'],
  },
  {
    id: 0,
    slug: 'blue-membership',
    name: 'Blue',
    price: 50000,
    discount: 15,
    perks: ['15% off every class & workshop', 'Annual membership party', '6311arts tote', '6311arts t-shirt'],
  },
];

export const MEMBERSHIPS: MembershipTier[] = TIERS.map((t) => ({ ...t, id: String(t.id) }));
