import { Instructor } from '../core/models';
import { slugify } from './mock-shop';

/**
 * Sample instructor profiles for the people teaching in `MOCK_CLASSES`, used only when WordPress
 * can't be reached. Bios are DRAFT copy written from the class listings themselves — staff replace
 * them (and add portraits, links and neighborhoods) in wp-admin before this goes live.
 */
const PROFILES: Omit<Instructor, 'slug'>[] = [
  {
    name: 'Candice Johnson',
    medium: 'Drawing & mixed media',
    bio: 'Candice teaches our drawing and mixed-media workshops, from scratchboard to Day of the Dead ofrendas, and sells her work in the 6311arts shop.',
    artistSlug: slugify('Candice Johnson'),
  },
  {
    name: 'Luis Valenzuela Jr.',
    medium: 'Talks & art history',
    bio: 'Luis leads our talks on art and activism, tracing how artists have organized and made the case for change.',
  },
  {
    name: 'Michelle Peterson-Albandoz',
    medium: 'Exhibiting artist',
    bio: 'Michelle shows at 6311arts and hosts the opening reception for “Boats & Pianos” in the East gallery.',
  },
  {
    name: 'Gwen Schilling',
    medium: 'Printmaking',
    bio: 'Gwen runs Intro to Screen Printing, the class most first-timers start with. Everyone leaves with something they pulled themselves.',
  },
  {
    name: 'Diego Granath',
    medium: 'Printmaking',
    bio: 'Diego teaches intermediate screen printing, bringing the press-room habits he uses at Rathaus into our studio.',
  },
  {
    name: 'Nicholas Waguespack',
    medium: 'Printmaking',
    bio: 'Nicholas teaches Sunday-morning screen printing and runs Printmaking Playground, the open session where you try a process without signing up for a course.',
  },
];

export const MOCK_INSTRUCTORS: Instructor[] = PROFILES.map((p) => ({ ...p, slug: slugify(p.name) })).sort((a, b) =>
  a.name.localeCompare(b.name),
);
