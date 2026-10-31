import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';
import { ArtClass, Exhibition, Product } from './models';
import { PreviewContext } from './preview';
import { SITE } from './site';

export interface PageSeo {
  title: string;
  description: string;
  /** Path starting with "/", used for the canonical URL and og:url. */
  path: string;
  image?: string;
  type?: 'website' | 'article' | 'product' | 'event';
  noindex?: boolean;
  /** Extra JSON-LD objects for this page. The organization graph is always included. */
  jsonLd?: object[];
  breadcrumbs?: { name: string; path: string }[];
}

/**
 * One place that writes every SEO tag. Runs during server rendering, so crawlers and link previews get
 * the full head without executing JavaScript.
 */
@Injectable({ providedIn: 'root' })
export class Seo {
  private title = inject(Title);
  private meta = inject(Meta);
  private doc = inject(DOCUMENT);
  private preview = inject(PreviewContext);

  set(p: PageSeo): void {
    // A staff preview is noindex whichever page is rendering inside it, and has no canonical address yet: the
    // record is not published, so the address in `path` is where it *will* be (ADR 0036).
    const previewing = this.preview.active();
    const fullTitle = p.path === '/' ? p.title : `${p.title} | ${SITE.name}`;
    const url = SITE.url + p.path;
    const image = p.image ?? SITE.ogImage;
    const description =
      p.description.length > 158 ? p.description.slice(0, 155).trimEnd() + '…' : p.description;

    this.title.setTitle(fullTitle);
    const tags: Record<string, string> = {
      description,
      robots: previewing
        ? 'noindex, nofollow'
        : p.noindex
          ? 'noindex, follow'
          : 'index, follow, max-image-preview:large',
      'og:title': fullTitle,
      'og:description': description,
      'og:url': url,
      'og:image': image,
      'og:type': p.type === 'product' ? 'product' : p.type === 'article' ? 'article' : 'website',
      'og:site_name': SITE.name,
      'og:locale': 'en_US',
      'twitter:card': 'summary_large_image',
      'twitter:title': fullTitle,
      'twitter:description': description,
      'twitter:image': image,
    };
    for (const [key, content] of Object.entries(tags)) {
      const attr = key.startsWith('og:') ? 'property' : 'name';
      this.meta.updateTag({ [attr]: key, content }, `${attr}="${key}"`);
    }

    if (previewing) this.unlink('canonical');
    else this.link('canonical', url);

    const graph: object[] = [organization(), ...(p.jsonLd ?? [])];
    if (p.breadcrumbs?.length)
      graph.push(breadcrumbs([{ name: 'Home', path: '/' }, ...p.breadcrumbs]));
    this.jsonLd(graph);
  }

  private link(rel: string, href: string): void {
    let el = this.doc.head.querySelector<HTMLLinkElement>(`link[rel="${rel}"]`);
    if (!el) {
      el = this.doc.createElement('link');
      el.setAttribute('rel', rel);
      this.doc.head.appendChild(el);
    }
    el.setAttribute('href', href);
  }

  private unlink(rel: string): void {
    this.doc.head.querySelector(`link[rel="${rel}"]`)?.remove();
  }

  private jsonLd(graph: object[]): void {
    this.doc.head.querySelectorAll('script[data-seo]').forEach((s) => s.remove());
    const script = this.doc.createElement('script');
    script.type = 'application/ld+json';
    script.setAttribute('data-seo', '');
    script.textContent = JSON.stringify({
      '@context': 'https://schema.org',
      '@graph': graph,
    }).replace(/</g, '\\u003c');
    this.doc.head.appendChild(script);
  }
}

// ---- schema.org builders ---------------------------------------------------------------------

const ORG_ID = `${SITE.url}/#org`;

function postalAddress() {
  return {
    '@type': 'PostalAddress',
    streetAddress: SITE.address.street,
    addressLocality: SITE.address.city,
    addressRegion: SITE.address.region,
    postalCode: SITE.address.postal,
    addressCountry: 'US',
  };
}

function place() {
  return { '@type': 'Place', name: SITE.name, address: postalAddress() };
}

export function organization() {
  return {
    '@type': ['ArtGallery', 'NGO'],
    '@id': ORG_ID,
    name: SITE.name,
    url: SITE.url,
    logo: SITE.logo,
    image: SITE.ogImage,
    description: SITE.description,
    telephone: SITE.phone,
    email: SITE.email,
    foundingDate: String(SITE.founded),
    address: postalAddress(),
    geo: { '@type': 'GeoCoordinates', latitude: SITE.geo.lat, longitude: SITE.geo.lng },
    sameAs: [SITE.instagram],
    openingHoursSpecification: SITE.hours
      .filter((h) => h.schema)
      .map((h) => ({
        '@type': 'OpeningHoursSpecification',
        dayOfWeek: h.schema!.days,
        opens: h.schema!.opens,
        closes: h.schema!.closes,
      })),
  };
}

function breadcrumbs(items: { name: string; path: string }[]) {
  return {
    '@type': 'BreadcrumbList',
    itemListElement: items.map((it, i) => ({
      '@type': 'ListItem',
      position: i + 1,
      name: it.name,
      item: SITE.url + it.path,
    })),
  };
}

export function eventLd(c: ArtClass, url: string) {
  return {
    '@type': c.type === 'Event' ? 'ExhibitionEvent' : 'EducationEvent',
    name: c.title,
    description: c.summary,
    startDate: c.start,
    endDate: c.end,
    eventStatus: 'https://schema.org/EventScheduled',
    eventAttendanceMode: 'https://schema.org/OfflineEventAttendanceMode',
    location: place(),
    image: c.image ? [c.image] : undefined,
    organizer: { '@id': ORG_ID },
    performer: c.instructor ? { '@type': 'Person', name: c.instructor } : undefined,
    // No offer until Square has priced the run: it can't be bought yet (ADR 0042).
    offers:
      c.price == null
        ? undefined
        : {
            '@type': 'Offer',
            url,
            price: (c.price / 100).toFixed(2),
            priceCurrency: 'USD',
            availability:
              c.spotsLeft === 0 ? 'https://schema.org/SoldOut' : 'https://schema.org/InStock',
            validFrom: '2026-01-01',
          },
  };
}

export function exhibitionLd(e: Exhibition) {
  return {
    '@type': 'ExhibitionEvent',
    name: e.title,
    description: e.summary,
    startDate: e.start,
    endDate: e.end,
    location: place(),
    image: e.image ? [e.image] : undefined,
    isAccessibleForFree: true,
    organizer: { '@id': ORG_ID },
  };
}

export function productLd(p: Product, url: string) {
  return {
    '@type': 'Product',
    name: p.name,
    description:
      p.description ||
      `${p.name}${p.artist ? ` by ${p.artist.name}` : ''}, an original work sold at ${SITE.name} in Chicago.`,
    image: p.image ? [p.image] : undefined,
    sku: String(p.id),
    brand: p.artist ? { '@type': 'Brand', name: p.artist.name } : { '@id': ORG_ID },
    // No offer until Square has priced the piece: it isn't for sale yet (ADR 0042).
    offers:
      p.price == null
        ? undefined
        : {
            '@type': 'Offer',
            url,
            price: (p.price / 100).toFixed(2),
            priceCurrency: 'USD',
            availability: p.inStock ? 'https://schema.org/InStock' : 'https://schema.org/SoldOut',
            seller: { '@id': ORG_ID },
          },
  };
}
