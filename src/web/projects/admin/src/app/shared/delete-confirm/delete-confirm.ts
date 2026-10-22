import { ConfirmService } from '../../core/confirm/confirm.service';

/** What the dialogs need to say about the record being deleted (ADR 0052). */
export interface DeleteConfirmRequest {
  /** The type, as a sentence names one: "page", "piece", "class". */
  readonly noun: string;
  /** What staff call this record: its title or name. */
  readonly label: string;
  /** Its web address, so the first dialog can name what stops working. */
  readonly slug: string;
  /** Whether the site is showing it now. */
  readonly live: boolean;
  /** Whether the form holds edits that will go with it. */
  readonly dirty: boolean;
  /**
   * Whether Square holds this record too (artwork, classes, membership plans — square.md). It gets a second
   * question of its own, because deleting it reaches past the CMS.
   */
  readonly inSquare: boolean;
  /** "Keep it" or "Keep them", for the record's own grammar. */
  readonly keepLabel: string;
}

/**
 * Until 5.5 ships the Square side (`square.catalog-delete-requested`), a delete frees the SKU and leaves the
 * Square object alone. The second dialog says so in one sentence, and that prompt removes this line.
 */
const SQUARE_NOT_YET =
  ' Until the Square sync ships, it stays in Square: retire it there too, or it will still be for sale.';

/**
 * Asks before a delete (ADR 0052): once about what leaves the site, and — for a record Square holds — a second
 * time about Square, because that half cannot be undone with a restore.
 *
 * Two questions only where the second one says something new. A page or an artist exists in the CMS alone, and a
 * second "are you sure" there is the kind of prompt people learn to click through.
 */
export async function confirmDelete(
  confirm: ConfirmService,
  request: DeleteConfirmRequest,
): Promise<boolean> {
  const unsaved = request.dirty ? 'Your unsaved changes go with it. ' : '';
  const leaves = request.live
    ? `6311arts.com/${request.slug} stops working within a minute, and the ${request.noun} leaves the CMS list. `
    : `The ${request.noun} leaves the CMS list. `;

  const first = await confirm.confirm({
    heading: `Delete this ${request.noun}?`,
    message: `${leaves}${unsaved}Its web address is free for another ${request.noun} straight away. An Admin can restore it from Deleted, as a draft. The deletion is recorded under your name.`,
    confirmLabel: `Delete ${request.noun}`,
    cancelLabel: request.keepLabel,
    tone: 'danger',
  });

  if (!first || !request.inSquare) {
    return first;
  }

  return confirm.confirm({
    heading: 'This also deletes it from Square',
    message: `“${request.label}” is sold through Square. Deleting it here deletes it from the Square catalogue as well, and restoring it in the CMS does not bring the Square object back — staff would set it up again.${SQUARE_NOT_YET} Orders, payments and anyone’s membership are untouched.`,
    confirmLabel: 'Delete here and in Square',
    cancelLabel: request.keepLabel,
    tone: 'danger',
  });
}
