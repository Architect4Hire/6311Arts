import { Exhibition } from '../core/models';
import { GALLERY_PHOTOS } from './venue-photos';

/**
 * Copied from 6311arts.com/gallery on 2026-10-18. Served only when the site API fails and SiteApi's mock fallback is on.
 *
 * Images are the show's own full-colour poster or install shot from the WordPress uploads folder. A show with
 * no artwork online yet borrows a photo of the room it will hang in, rather than falling back to a blank tile.
 */
const img = (path: string, size = 'fit=1200%2C1200') =>
  `https://i0.wp.com/6311arts.com/wp-content/uploads/${path}?${size}&ssl=1`;

export const MOCK_EXHIBITIONS: Exhibition[] = [
  {
    slug: 'local-high-school-art-exhibition',
    title: 'Local High School Art Exhibition',
    start: '2026-12-12',
    end: '2027-01-30',
    summary: 'Year-end work by students from the high schools around us. The next generation of North Side artists, on our walls.',
    artists: [],
    image: GALLERY_PHOTOS[0],
  },
  {
    slug: 'boats-and-pianos',
    title: 'Boats & Pianos',
    start: '2026-10-24',
    end: '2026-12-05',
    summary:
      'New work by Michelle Peterson-Albandoz: oversized constructions built from reclaimed wood, boats and pianos, alongside new multimedia pieces.',
    artists: ['Michelle Peterson-Albandoz'],
    image: img('2026/08/Boats-and-Pianos-1.jpg'),
    reception: { start: '2026-10-24T18:00:00-05:00', end: '2026-10-24T21:00:00-05:00', note: 'Free and open to all' },
  },
  {
    slug: 'portrait-mode',
    title: 'Portrait Mode',
    start: '2026-08-29',
    end: '2026-10-17',
    summary:
      'A group show about capturing people across mediums and styles, with Brian Eaton’s “Trying to Keep It All Together” in the East and West galleries.',
    artists: ['Harry Sudman', 'Jeffrey Brooks', 'Eddwin Meyers', 'Alejandra Guerrero', 'Hector Hernandez', 'Adrian Zavala', 'Brian Eaton'],
    image: img('2026/08/Untitled_Artwork-3.jpeg', 'fit=768%2C1187'),
  },
  {
    slug: 'nooks-and-crannies',
    title: 'Nooks and Crannies',
    start: '2026-07-11',
    end: '2026-08-22',
    summary: 'A solo show by Jeremy Humbert, with work by Mike Famous, Laurence Hoffman, Candice Johnson and Kevin Johnson.',
    artists: ['Jeremy Humbert', 'Candice Johnson', 'Kevin Johnson', 'Piloto'],
    image: img('2026/08/IMG_6232-2.jpeg', 'resize=663%2C1024'),
  },
  {
    slug: 'queue',
    title: 'Queue',
    start: '2026-05-16',
    end: '2026-07-04',
    summary: 'A solo show by Brad Gibbs, with Austin Special Chicago and DePaul Artists Coalition in the East and West galleries.',
    artists: ['Brad Gibbs', 'Piloto'],
    image: img('2026/08/IMG_6233.jpeg', 'resize=663%2C1024'),
  },
  {
    slug: 'the-observed-and-the-imagined',
    title: 'The Observed & The Imagined',
    start: '2026-03-28',
    end: '2026-05-09',
    summary: 'Yvonne Doll and Victoria Loeb pair figure and abstraction, with ten North Side photographers.',
    artists: ['Yvonne Doll', 'Victoria Loeb', 'Justin Matthew', 'Eddie Pedroza', 'Mike Sebahar'],
    image: img('2026/08/IMG_5794.jpeg', 'resize=1024%2C731'),
  },
  {
    slug: 'a-community-of-artists-ii',
    title: 'A Community of Artists II',
    start: '2026-02-07',
    end: '2026-03-21',
    summary: 'Seventeen neighborhood artists, one room.',
    artists: ['Annette Baksinskas'],
    image: img('2026/08/IMG_5724.jpeg', 'resize=663%2C1024'),
  },
  {
    slug: 'grand-opening',
    title: 'Grand Opening',
    start: '2025-11-22',
    end: '2026-01-31',
    summary: 'Thirty local artists opened the doors of the old funeral home on Clark Street.',
    artists: ['Lisa Barcy', 'Stephen Bell'],
    image: img('2026/08/IMG_4675.jpeg', 'resize=1024%2C827'),
  },
];
