import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';

/** How long a saved file's object URL lives: long enough for any browser to have read it. */
const REVOKE_AFTER_MS = 60_000;

/**
 * Hands the person a file the CMS fetched (the newsletter export): an object URL clicked through a temporary
 * `download` link, then revoked. Behind an injectable, like `BrowserNavigation`, so components never touch
 * `window` and tests can see what was saved without a real download.
 */
@Injectable({ providedIn: 'root' })
export class BrowserDownload {
  private readonly document = inject(DOCUMENT);

  save(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const link = this.document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.rel = 'noopener';
    link.hidden = true;
    this.document.body.appendChild(link);
    try {
      link.click();
    } finally {
      link.remove();
      // Some browsers read the file after the click returns, so the URL lives a while before its memory is freed.
      setTimeout(() => URL.revokeObjectURL(url), REVOKE_AFTER_MS);
    }
  }
}
