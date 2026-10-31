import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { ArtistPage, Instructor, Product, ProductPage } from '../../core/models';
import { ProductQuery, SiteApi } from '../../services/site-api.service';
import { ArtistDetail } from './artist-detail';

function piece(n: number): Product {
  return {
    id: `p${n}`,
    slug: `piece-${n}`,
    name: `Piece ${n}`,
    artist: { name: 'Carrie Haase', slug: 'carrie-haase' },
    price: 10000,
    categories: [],
    inStock: true,
  };
}

const CARRIE: ArtistPage = {
  artist: {
    slug: 'carrie-haase',
    name: 'Carrie Haase',
    medium: 'Jewelry',
    bio: ['Metalsmith in Edgewater.', 'She hammers copper.'],
    active: true,
  },
  works: [piece(1), piece(2)],
  nextCursor: 'c2',
};

const TEACHER: Instructor = { slug: 'carrie', name: 'Carrie Haase', artistSlug: 'carrie-haase' };

async function render(
  page: ArtistPage | undefined,
  more: (q: ProductQuery) => ProductPage = () => ({ items: [], total: 0, nextCursor: null }),
) {
  // The hero's slideshow asks whether motion is reduced; jsdom has no matchMedia.
  vi.stubGlobal('matchMedia', () => ({
    matches: true,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
  const products = vi.fn((q: ProductQuery) => of(more(q)));
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideRouter(
        [{ path: 'artists/:slug', component: ArtistDetail }],
        withComponentInputBinding(),
      ),
      {
        provide: SiteApi,
        useValue: { artist: () => of(page), instructors: () => of([TEACHER]), products },
      },
    ],
  });
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl('/artists/carrie-haase');
  const element = harness.routeNativeElement!;
  const text = (selector: string) =>
    [...element.querySelectorAll(selector)].map((e) => e.textContent!.replace(/\s+/g, ' ').trim());
  return { harness, element, text, products };
}

describe('ArtistDetail', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows the CMS profile: bio paragraphs, medium, gallery status and the work in the shop', async () => {
    const { element, text } = await render(CARRIE);

    expect(text('.detail__title')).toEqual(['Carrie Haase']);
    expect(text('.detail__lead')).toEqual(['Metalsmith in Edgewater.', 'She hammers copper.']);
    const facts = text('.facts li');
    expect(facts).toContain('Works in Jewelry');
    expect(facts.some((f) => f.startsWith('Showing now at the gallery'))).toBe(true);
    expect(facts).toContain('2+ pieces in the shop');
    expect(facts).toContain('Also teaches here — see their classes');
    expect(element.querySelectorAll('#work app-product-card').length).toBe(2);
  });

  it('links an artist who also teaches to their instructor page', async () => {
    const { element } = await render(CARRIE);
    const link = [...element.querySelectorAll('a')].find(
      (a) => a.textContent === 'see their classes',
    );
    expect(link?.getAttribute('href')).toBe('/instructors/carrie');
  });

  it('pages in more work from the shop list, and drops the button on the last page', async () => {
    const { harness, element, products } = await render(CARRIE, () => ({
      items: [piece(3)],
      total: 3,
      nextCursor: null,
    }));
    expect(element.querySelectorAll('app-product-card').length).toBe(2);

    element.querySelector<HTMLButtonElement>('#work button')!.click();
    await harness.fixture.whenStable();

    expect(products).toHaveBeenCalledWith({ artist: 'carrie-haase', cursor: 'c2' });
    expect(element.querySelectorAll('app-product-card').length).toBe(3);
    expect(element.querySelector('#work button')).toBeNull();
  });

  it('writes a holding line for an artist with no bio, and says when nothing is for sale', async () => {
    const { text } = await render({
      artist: { slug: 'carrie-haase', name: 'Carrie Haase', bio: [] },
      works: [],
      nextCursor: null,
    });

    expect(text('.detail__lead')[0]).toContain('We’re still writing their profile');
    expect(text('.facts li')).toContain('Nothing in the shop right now');
    expect(text('.empty')[0]).toContain('New work from Carrie Haase is on its way');
  });

  it('is the not-found page for an artist the API does not have', async () => {
    const { element } = await render(undefined);
    expect(element.querySelector('app-not-found')).not.toBeNull();
    expect(element.querySelector('.detail')).toBeNull();
  });
});
