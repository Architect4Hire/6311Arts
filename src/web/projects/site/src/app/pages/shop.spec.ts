import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { Product, ProductPage } from '../core/models';
import { ProductQuery, SiteApi } from '../services/site-api.service';
import { ProductDetail, Shop } from './shop';

const CUFF: Product = {
  id: 'f1d2',
  slug: 'copper-cuff',
  name: 'Copper Cuff',
  artist: { name: 'Carrie Haase', slug: 'carrie-haase' },
  price: 26500,
  categories: ['jewelry'],
  inStock: true,
};

/** A shop of 312 works whose every page is CUFF, resuming at `next-<page>`. */
function pageFor(q: ProductQuery): ProductPage {
  const at = q.cursor ? Number(q.cursor.split('-')[1]) : 1;
  return { items: [{ ...CUFF, id: `${CUFF.id}-${at}` }], total: 312, nextCursor: `c-${at + 1}` };
}

async function setup(
  url: string,
  products = vi.fn((q: ProductQuery) => of(pageFor(q))),
  product?: Product,
) {
  // The hero's slideshow asks whether motion is reduced; jsdom has no matchMedia.
  vi.stubGlobal('matchMedia', () => ({
    matches: true,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideRouter([
        { path: 'shop', component: Shop },
        { path: 'shop/:slug', component: ProductDetail },
      ]),
      { provide: SiteApi, useValue: { products, product: () => of(product) } },
    ],
  });
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(url);
  const element = harness.routeNativeElement!;
  const text = (selector: string) =>
    element.querySelector(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const link = (label: string) =>
    [...element.querySelectorAll<HTMLAnchorElement>('a')].find(
      (a) => a.textContent?.trim() === label,
    );
  return { harness, element, products, text, link };
}

describe('Shop', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows a piece Square has not priced as price to be announced', async () => {
    const { element } = await setup(
      '/shop',
      vi.fn(() => of({ items: [{ ...CUFF, price: undefined }], total: 1, nextCursor: null })),
    );
    expect(element.querySelector('.product-card__price')).toBeNull();
    expect(element.querySelector('.product-card__tba')?.textContent).toBe('Price to be announced');
  });

  it('lands a page turn on the new works, focused, instead of the top of the page', async () => {
    const scrolled: Element[] = [];
    Element.prototype.scrollIntoView = function (this: Element) {
      scrolled.push(this);
    };
    const { harness, element, link, products } = await setup('/shop');

    link('Next')!.click();
    await harness.fixture.whenStable();

    expect(products).toHaveBeenLastCalledWith(expect.objectContaining({ cursor: 'c-2' }));
    const count = element.querySelector('#works');
    expect(document.activeElement).toBe(count);
    expect(scrolled).toEqual([count]);
    expect(TestBed.inject(Router).url).toBe('/shop?page=2&cursor=c-2');
  });

  it('changes the category in place, without moving the visitor or their focus', async () => {
    const scrolled: Element[] = [];
    Element.prototype.scrollIntoView = function (this: Element) {
      scrolled.push(this);
    };
    const { harness, element, link, products } = await setup('/shop?page=2&cursor=c-2');

    link('Prints')!.click();
    await harness.fixture.whenStable();

    expect(products).toHaveBeenLastCalledWith(expect.objectContaining({ category: 'prints', cursor: undefined }));
    expect(TestBed.inject(Router).url).toBe('/shop?category=prints');
    expect(document.activeElement).not.toBe(element.querySelector('#works'));
    expect(scrolled).toEqual([]);
  });

  it('leaves a modified click to the browser, for a new tab', async () => {
    const { link, products } = await setup('/shop');
    const calls = products.mock.calls.length;
    const event = new MouseEvent('click', { bubbles: true, cancelable: true, ctrlKey: true });
    link('Next')!.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(false);
    expect(products.mock.calls.length).toBe(calls);
  });

  it('says how many works there are and pages through them by cursor', async () => {
    const { products, text, link } = await setup('/shop?category=jewelry');

    expect(products).toHaveBeenCalledWith({
      category: 'jewelry',
      sort: undefined,
      cursor: undefined,
      limit: 12,
    });
    expect(text('.filters__count')).toBe('312 works');
    expect(text('.pager span')).toBe('Page 1 of 26');
    expect(link('Previous')).toBeUndefined();
    expect(link('Next')?.getAttribute('href')).toBe('/shop?category=jewelry&page=2&cursor=c-2');
  });

  it('goes back a page with the cursor it came forward with', async () => {
    const { harness, text, link } = await setup('/shop?page=2&cursor=c-2');
    await harness.navigateByUrl('/shop?page=3&cursor=c-3');

    expect(text('.pager span')).toBe('Page 3 of 26');
    expect(link('Previous')?.getAttribute('href')).toBe('/shop?page=2&cursor=c-2');
    expect(link('Previous')?.getAttribute('rel')).toBe('prev');
  });

  it('offers the first page to someone who landed deep in the list, since there is no way back to page 2', async () => {
    const { link } = await setup('/shop?page=5&cursor=c-5');
    expect(link('Previous')).toBeUndefined();
    expect(link('First page')?.getAttribute('href')).toBe('/shop');
  });

  it('reads an old page number without a cursor as the first page', async () => {
    const { products, text } = await setup('/shop?page=3');
    expect(products).toHaveBeenCalledWith(expect.objectContaining({ cursor: undefined }));
    expect(text('.pager span')).toBe('Page 1 of 26');
  });

  it('passes a known order through and ignores one it does not know', async () => {
    const { harness, products } = await setup('/shop?sort=price-desc');
    expect(products).toHaveBeenLastCalledWith(expect.objectContaining({ sort: 'price-desc' }));
    await harness.navigateByUrl('/shop?sort=cheapest');
    expect(products).toHaveBeenLastCalledWith(expect.objectContaining({ sort: undefined }));
  });

  it('shows no pager when everything fits on one page', async () => {
    const { element } = await setup(
      '/shop',
      vi.fn(() => of({ items: [CUFF], total: 1, nextCursor: null })),
    );
    expect(element.querySelector('.pager')).toBeNull();
  });
});

describe('ProductDetail', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('buys through WooCommerce’s product page until Phase 5', async () => {
    const { link } = await setup('/shop/copper-cuff', undefined, CUFF);
    expect(link('Buy now')?.getAttribute('href')).toBe('https://6311arts.com/product/copper-cuff/');
  });

  it('offers nothing to buy until Square has priced the piece', async () => {
    const { link, text } = await setup('/shop/copper-cuff', undefined, {
      ...CUFF,
      price: undefined,
    });
    expect(link('Buy now')).toBeUndefined();
    expect(text('.buy__price')).toBe('');
    expect(text('.buy__tba')).toBe('Price to be announced');
  });

  it('credits an artist with no page by name, without a link', async () => {
    const { text, element } = await setup('/shop/copper-cuff', undefined, {
      ...CUFF,
      artist: { name: 'Zed Draft' },
    });
    expect(text('.detail__by')).toBe('by Zed Draft');
    expect(element.querySelector('.detail__by a')).toBeNull();
  });

  it('shows Sold rather than a button once Square says it has sold', async () => {
    const { link, text } = await setup('/shop/copper-cuff', undefined, { ...CUFF, inStock: false });
    expect(link('Buy now')).toBeUndefined();
    expect(text('.tag--sold')).toBe('Sold');
  });
});
