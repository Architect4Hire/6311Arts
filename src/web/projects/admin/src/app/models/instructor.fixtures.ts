import { Instructor, InstructorSummary } from './instructor';

/** One draft instructor as the API returns it, for specs. */
export const INSTRUCTOR: Instructor = {
  id: '0199f0a0-0000-7000-8000-0000000000a1',
  name: 'Gwen Schilling',
  slug: 'gwen-schilling',
  medium: 'Printmaking',
  bio: 'Gwen runs Intro to Screen Printing.',
  active: true,
  portraitMediaId: null,
  website: null,
  instagram: null,
  artist: null,
  status: 'Draft',
  publishedAt: null,
  scheduledFor: null,
  slugLocked: false,
  legacyWordPressId: null,
  createdAt: '2026-10-20T12:00:00+00:00',
  updatedAt: '2026-10-20T12:00:00+00:00',
  updatedBy: 'editor-1',
  etag: '"0123456789abcdef0123456789abcdef"',
};

export function toInstructorSummary(instructor: Instructor): InstructorSummary {
  return {
    id: instructor.id,
    name: instructor.name,
    slug: instructor.slug,
    medium: instructor.medium,
    active: instructor.active,
    status: instructor.status,
    portraitMediaId: instructor.portraitMediaId,
    updatedAt: instructor.updatedAt,
  };
}
