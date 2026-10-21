import { Injectable } from '@angular/core';

/** Where checkout, cart and membership sign-up still live until Phase 5 brings them onto the platform. */
const WOOCOMMERCE_ORIGIN = 'https://6311arts.com';

/**
 * The spike's links into WooCommerce, kept as they were until Phase 5 replaces them with on-site checkout
 * (Web Payments SDK) and Square subscriptions. One change was forced: WooCommerce's numeric product ids are gone
 * with the move to the site read API (ADR 0042 keeps no legacy id), so a Buy link opens the WooCommerce product page
 * by its slug — the spike's own fallback — rather than adding the id to the cart.
 */
@Injectable({ providedIn: 'root' })
export class LegacyStore {
  cartUrl(): string {
    return `${WOOCOMMERCE_ORIGIN}/cart/`;
  }

  buyUrl(item: { slug: string }): string {
    return `${WOOCOMMERCE_ORIGIN}/product/${item.slug}/`;
  }
}
