import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';

/**
 * Full-page navigation, for leaving this app (the CMS is another one on the same origin). Behind an injectable
 * so tests can see where the site sends people without navigating the test page away.
 */
@Injectable({ providedIn: 'root' })
export class BrowserLocation {
  private readonly document = inject(DOCUMENT);

  assign(url: string): void {
    this.document.location.assign(url);
  }
}
