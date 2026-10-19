import { InjectionToken } from '@angular/core';

/** Everything about the organization that appears on more than one page. */
export const SITE = {
  name: '6311arts',
  tagline: 'art & community',
  url: 'https://6311arts.com',
  description:
    'An inclusive art center on Chicago’s North Side. Gallery, shop and classes taught by local artists in a former funeral home at 6311 N. Clark St.',
  phone: '872-370-4500',
  email: 'info@6311arts.com',
  instagram: 'https://www.instagram.com/6311arts',
  address: {
    street: '6311 N. Clark St.',
    city: 'Chicago',
    region: 'IL',
    postal: '60660',
    neighborhood: 'Rogers Edge, Edgewater',
  },
  geo: { lat: 41.9973, lng: -87.6699 },
  mapUrl: 'https://maps.google.com/?q=6311+N+Clark+St,+Chicago,+IL+60660',
  hours: [
    { days: 'Mon – Tue', time: 'By appointment', schema: null },
    { days: 'Wednesday', time: '11 am – 5 pm', schema: { days: ['Wednesday'], opens: '11:00', closes: '17:00' } },
    {
      days: 'Thu – Sat',
      time: '11 am – 6 pm',
      schema: { days: ['Thursday', 'Friday', 'Saturday'], opens: '11:00', closes: '18:00' },
    },
    { days: 'Sunday', time: '11 am – 5 pm', schema: { days: ['Sunday'], opens: '11:00', closes: '17:00' } },
  ],
  founded: 2025,
  submitFormUrl:
    'https://docs.google.com/forms/d/e/1FAIpQLSeaKS9y3DIm2oTTM3LSvl4EneK-24XxNRxuN-UlrOlrYexNpw/viewform',
  ogImage: 'https://i0.wp.com/6311arts.com/wp-content/uploads/2026/08/Boats-and-Pianos-1.jpg?fit=1200%2C1142&ssl=1',
  logo: 'https://6311arts.com/wp-content/uploads/2026/07/6311artsBLKLINE.png',
} as const;

export interface ApiConfig {
  /** WordPress origin that serves /wp-json. Checkout and cart stay on this origin. */
  wpOrigin: string;
  /** How long to wait for WordPress before falling back to bundled sample data. */
  timeoutMs: number;
  /** Force sample data, e.g. for local design work with no network. */
  mockOnly: boolean;
}

export const API_CONFIG = new InjectionToken<ApiConfig>('API_CONFIG', {
  providedIn: 'root',
  factory: () => ({ wpOrigin: 'https://6311arts.com', timeoutMs: 4000, mockOnly: false }),
});
