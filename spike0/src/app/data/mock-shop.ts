import { Artist, MembershipTier, Product } from '../core/models';

/** Sample of the shop and artist list copied from 6311arts.com on 2026-10-18. Used when the API can't be reached. */
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

export const MOCK_PRODUCTS: Product[] = [
  { id: 9101, slug: 'ammonite-fossil-and-variscite-oxidized-silver-necklace-22chain-carrie-haase', name: 'Ammonite Fossil & Variscite Necklace', artist: a('Carrie Haase'), price: 52500, image: up('2026/09/Ammonite-Fossil-and-Variscite-Oxidized-Silver-Necklace-22chain-necklace.jpeg'), categories: ['jewelry'], inStock: true },
  { id: 9102, slug: 'large-branch-handle-bowl-susan-volk', name: 'Large Branch Handle Bowl', artist: a('Susan Volk'), price: 27500, categories: ['ceramics'], inStock: true },
  { id: 9103, slug: 'the-recovery-series-pt-2-6-stephen-bell', name: 'The Recovery Series (Pt. 2) #6', artist: a('Stephen Bell'), price: 12500, categories: ['sculpture'], inStock: true },
  { id: 9104, slug: 'australian-opal-silver-ring-carrie-haase', name: 'Australian Opal Silver Ring', artist: a('Carrie Haase'), price: 26500, image: up('2026/09/Australian-Opal-Silver-Ring-jewelry.jpeg'), categories: ['jewelry'], inStock: true },
  { id: 9105, slug: 'mushrooms-in-fall-audrey-avril-2', name: 'Mushrooms in Fall (print)', artist: a('Audrey Avril'), price: 1000, categories: ['prints'], inStock: true },
  { id: 9106, slug: 'peekaboo-sterling-oval-ring-size-9-carrie-haase', name: 'Peekaboo Sterling Oval Ring', artist: a('Carrie Haase'), price: 30000, image: up('2026/09/Peekaboo-Sterling-Oval-Ring-size-9-jewelry.jpeg'), categories: ['jewelry'], inStock: true },
  { id: 7998, slug: 'acrylic-painting-on-board-rectangle', name: 'Acrylic Painting on Board', artist: a('Aaron Wooten'), price: 7500, categories: ['painting'], inStock: true },
  { id: 9107, slug: 'grandma-turn-me-into-a-newt-next-audrey-avril', name: 'Grandma (Turn Me Into a Newt Next)', artist: a('Audrey Avril'), price: 800, categories: ['prints'], inStock: true },
  { id: 9108, slug: 'copper-cuff-carrie-haase', name: 'Copper Cuff', artist: a('Carrie Haase'), price: 9500, image: up('2026/09/Copper-Cuff-jewelry.jpeg'), categories: ['jewelry'], inStock: true },
  { id: 9109, slug: 'anabelles-first-summon-audrey-avril', name: 'Anabelle’s First Summon (print)', artist: a('Audrey Avril'), price: 1000, categories: ['prints'], inStock: true },
  { id: 9110, slug: 'lodolite-pendant-carrie-haase', name: 'Lodolite Pendant', artist: a('Carrie Haase'), price: 18500, image: up('2026/09/Lodolite-Pendant-jewelry.jpeg'), categories: ['jewelry'], inStock: true },
  { id: 8000, slug: 'plantimals-signed-hardback-book', name: 'Plantimals (signed hardback)', artist: a('Robert Stevenson'), price: 1500, categories: ['books'], inStock: true },
  { id: 9111, slug: 'the-guide-audrey-avril-2', name: 'The Guide (print)', artist: a('Audrey Avril'), price: 800, categories: ['prints'], inStock: true },
  { id: 9112, slug: 'serpentine-pendant-carrie-haase', name: 'Serpentine Pendant', artist: a('Carrie Haase'), price: 16500, image: up('2026/09/Serpentine-Pendant-jewelry.jpeg'), categories: ['jewelry'], inStock: true },
  { id: 9113, slug: 'minimalist-sterling-silver-hexagon-studs-carrie-haase', name: 'Sterling Hexagon Studs', artist: a('Carrie Haase'), price: 13500, image: up('2026/09/Minimalist-Sterling-Silver-Hexagon-Studs-earrings.jpeg'), categories: ['jewelry'], inStock: true },
  { id: 7999, slug: 'plantimals-signed-hardbound-book', name: 'Spaced Out Inner Galactic Beings (signed)', artist: a('Robert Stevenson'), price: 1200, categories: ['books'], inStock: true },
  { id: 9114, slug: 'iron-buffalo-ring-carrie-haase', name: 'Iron Buffalo Ring', artist: a('Carrie Haase'), price: 14500, image: up('2026/09/Iron-Buffalo-Ring-jewelry.jpeg'), categories: ['jewelry'], inStock: false },
  { id: 9115, slug: 'petrified-palmwood-earrings-carrie-haase', name: 'Petrified Palmwood Earrings', artist: a('Carrie Haase'), price: 11000, image: up('2026/09/Petrified-Palmwood-Earrings-Carrie-Haase.jpeg'), categories: ['jewelry'], inStock: true },
];

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

export const MEMBERSHIPS: MembershipTier[] = [
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
