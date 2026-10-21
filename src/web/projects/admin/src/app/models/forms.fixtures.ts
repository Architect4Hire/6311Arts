import { ContactMessage, NewsletterSubscriber, Submission, SubmissionSummary } from './forms';

/** An artist submission as the API returns it, for specs: selling and teaching, two pictures. */
export const SUBMISSION: Submission = {
  id: '0199f0a0-0000-7000-8000-0000000000c1',
  name: 'Rosa Ortiz',
  email: 'rosa@example.com',
  wants: ['sell', 'teach'],
  answers: {
    pronouns: 'she/her',
    phone: '773-555-0100',
    neighborhood: 'Rogers Park',
    zip: '60626',
    website: 'https://rosaortiz.example',
    instagram: 'rosa.makes',
    medium: 'Ceramics',
    otherMedia: null,
    years: 12,
    priceBand: '$100 – $300',
    bio: 'Potter on Clark Street.',
    statement: 'Wheel-thrown stoneware.\nGlazes from local ash.',
    sell: { piecesReady: 20, restock: 'limited' },
    exhibit: null,
    teach: {
      classTitle: 'Wheel basics',
      maxStudents: 8,
      format: 'Three-week course',
      materials: 'Clay provided.',
      taughtBefore: true,
    },
    portfolio: 'https://portfolio.example/rosa',
    heard: 'Walked past the gallery',
    newsletter: true,
  },
  images: [
    {
      id: '0199f0a0-0000-7000-8000-0000000000d1',
      fileName: 'bowl.jpg',
      width: 2400,
      height: 1600,
      originalUrl: '/media/0199f0a0-0000-7000-8000-0000000000d1/original?v=aaaaaaaaaaaa',
      thumbnailUrl: '/media/0199f0a0-0000-7000-8000-0000000000d1/400.webp?v=bbbbbbbbbbbb',
    },
    {
      id: '0199f0a0-0000-7000-8000-0000000000d2',
      fileName: null,
      width: 1200,
      height: 1200,
      originalUrl: '/media/0199f0a0-0000-7000-8000-0000000000d2/original?v=cccccccccccc',
      thumbnailUrl: null,
    },
  ],
  status: 'New',
  notes: null,
  receivedAt: '2026-10-20T15:30:00+00:00',
  updatedAt: '2026-10-20T15:30:00+00:00',
  updatedBy: null,
  etag: '"0123456789abcdef0123456789abcdef"',
};

export function toSubmissionSummary(submission: Submission): SubmissionSummary {
  return {
    id: submission.id,
    name: submission.name,
    email: submission.email,
    wants: submission.wants,
    status: submission.status,
    imageCount: submission.images.length,
    receivedAt: submission.receivedAt,
  };
}

/** A contact message as the API returns it, for specs. */
export const CONTACT_MESSAGE: ContactMessage = {
  id: '0199f0a0-0000-7000-8000-0000000000e1',
  name: 'Sam Lee',
  email: 'sam@example.com',
  message: 'Are you open Sunday?\nI would like to visit with my class.',
  handled: false,
  receivedAt: '2026-10-20T16:00:00+00:00',
  updatedAt: '2026-10-20T16:00:00+00:00',
  updatedBy: null,
  etag: '"fedcba9876543210fedcba9876543210"',
};

/** A newsletter address as the API returns it, for specs. */
export const SUBSCRIBER: NewsletterSubscriber = {
  id: '0199f0a0-0000-7000-8000-0000000000f1',
  email: 'reader@example.com',
  consentedAt: '2026-10-21T14:00:00+00:00',
  consentVersion: '2026-10-21',
  source: 'Signup',
  createdAt: '2026-10-21T14:00:00+00:00',
};
