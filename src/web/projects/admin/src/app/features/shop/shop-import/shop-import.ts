import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiControl, UiField, UiNotice, UiStatusChip } from '@6311/ui';
import { BrowserDownload } from '../../../core/browser/browser-download';
import { HasUnsavedChanges, warnBeforeUnload } from '../../../core/guards/unsaved-changes.guard';
import {
  SHOP_CODES,
  SHOP_IMPORT_COLUMNS,
  SHOP_IMPORT_LIMITS,
  SHOP_IMPORT_TEMPLATE_FILE_NAME,
  ShopImportPreview,
  ShopImportResult,
  ShopImportRow,
  categoryLabel,
  shopImportFileName,
  shopImportTemplate,
} from '../../../models/shop';
import { ShopService } from '../../../services/shop.service';
import { MessageOptions, messageOf, problemOf } from '../../../core/http/problem-message';

type Phase = 'idle' | 'checking' | 'ready' | 'importing' | 'done';

/** A file problem the page found itself, or the API's: a heading and the reasons under it. */
interface FileProblem {
  readonly heading: string;
  readonly reasons: readonly string[];
}

/** How a refused import is put: nothing here is lost by a dropped connection, and a 403 names what was refused. */
const REFUSALS: MessageOptions = {
  edits: false,
  forbidden: 'You don’t have permission to import artwork.',
};

const PRICE = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' });

const COUNT = new Intl.NumberFormat('en-US');

function pieces(count: number): string {
  return `${COUNT.format(count)} ${count === 1 ? 'piece' : 'pieces'}`;
}

/**
 * Many pieces at once from a spreadsheet (`/admin/shop/import`): download the template, fill it in, choose the file,
 * and the API says what each row would become and what is wrong with it. Nothing is written until every row is
 * right, and then every row is created as a Draft in one go, or none is (ADR 0053).
 *
 * The file's text is sent as it is; the API reads and checks it, so the page has no CSV parser of its own. Photographs
 * are not part of a CSV: staff add them, and publish, from each piece's editor.
 */
@Component({
  selector: 'cms-shop-import',
  imports: [RouterLink, UiButton, UiControl, UiField, UiNotice, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shop-import.html',
  styleUrl: './shop-import.scss',
})
export class ShopImport implements HasUnsavedChanges {
  private readonly shop = inject(ShopService);
  private readonly download = inject(BrowserDownload);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  /** The chosen file's text, kept here only: it is sent again, unchanged, by the import. */
  private csv = '';

  /** The file's name as the API takes it for the audit trail (`shopImportFileName`), or `null`. */
  private sentFileName: string | null = null;

  /**
   * One key per file chosen, so pressing Import again after a dropped answer replays the first result rather than
   * importing twice (api-contract.md "Idempotency"). Choosing a file, even the same one again, makes a new key.
   */
  private idempotencyKey = '';

  /** Bumped by each file chosen, so a preview of a file since replaced is dropped. */
  private generation = 0;

  protected readonly columns = SHOP_IMPORT_COLUMNS;
  protected readonly limits = SHOP_IMPORT_LIMITS;
  protected readonly phase = signal<Phase>('idle');
  protected readonly fileName = signal<string | null>(null);
  protected readonly preview = signal<ShopImportPreview | null>(null);
  protected readonly result = signal<ShopImportResult | null>(null);
  protected readonly fileProblem = signal<FileProblem | null>(null);
  /** Why the import itself was refused, shown above the preview it re-read. */
  protected readonly importError = signal<string | null>(null);
  protected readonly liveMessage = signal('');

  protected readonly canImport = computed(() => {
    const preview = this.preview();
    return (
      preview !== null &&
      preview.rowCount > 0 &&
      preview.errorCount === 0 &&
      this.phase() === 'ready'
    );
  });

  protected readonly summary = computed(() => {
    const preview = this.preview();
    if (preview === null) return '';
    if (preview.errorCount === 0) {
      return `${COUNT.format(preview.rowCount)} ${preview.rowCount === 1 ? 'row is' : 'rows are'} ready to import.`;
    }
    return `${COUNT.format(preview.errorCount)} of ${COUNT.format(preview.rowCount)} ${
      preview.rowCount === 1 ? 'row needs' : 'rows need'
    } fixing. Correct the file and choose it again.`;
  });

  protected readonly importLabel = computed(() => {
    const count = this.preview()?.rowCount ?? 0;
    return `Import ${pieces(count)}`;
  });

  protected readonly createdMessage = computed(() => {
    const result = this.result();
    return result === null ? '' : `Imported ${pieces(result.createdCount)} as drafts.`;
  });

  constructor() {
    warnBeforeUnload(() => this.hasUnsavedChanges());
  }

  /** Leaving mid-import would hide its answer: the import still finishes, but nobody would see how. */
  hasUnsavedChanges(): boolean {
    return this.phase() === 'importing';
  }

  unsavedChangesMessage(): string {
    return 'The import is still running. If you leave now you won’t see whether it worked.';
  }

  /** Saves the template: the columns and two example rows to overwrite. */
  protected downloadTemplate(): void {
    const blob = new Blob([shopImportTemplate()], { type: 'text/csv;charset=utf-8' });
    this.download.save(blob, SHOP_IMPORT_TEMPLATE_FILE_NAME);
    this.liveMessage.set(`Downloaded ${SHOP_IMPORT_TEMPLATE_FILE_NAME}.`);
  }

  protected async onChoose(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    if (!file) return;

    // Emptied so choosing the same file again (fixed in the meantime) is a change too. The File stays readable.
    input.value = '';

    const generation = ++this.generation;
    this.clear();
    this.fileName.set(file.name);
    this.phase.set('checking');
    this.liveMessage.set(`Checking ${file.name}…`);

    let text: string;
    try {
      text = await file.text();
    } catch {
      if (generation !== this.generation) return;
      this.refuse('The file couldn’t be read', ['Choose it again, or save a fresh copy first.']);
      return;
    }
    if (generation !== this.generation) return;

    // Checked here so a file the API would refuse is not sent at all, and the reason is the one staff can act on.
    if (text.includes('\uFFFD')) {
      this.refuse('This file isn’t saved as UTF-8', [
        'Some characters couldn’t be read. In Excel, choose Save As → CSV UTF-8 (comma delimited), then choose the new file.',
      ]);
      return;
    }
    if (text.length > this.limits.maxCharacters) {
      this.refuse('This file is too large', [
        `A file can hold up to ${COUNT.format(this.limits.maxCharacters)} characters. Split it into smaller files of at most ${COUNT.format(this.limits.maxRows)} rows.`,
      ]);
      return;
    }

    this.csv = text;
    this.sentFileName = shopImportFileName(file.name);
    this.idempotencyKey = newIdempotencyKey();
    await this.runPreview(generation);
  }

  /** Imports every row as a Draft. Refused unless the last preview found nothing wrong. */
  protected async importAll(): Promise<void> {
    if (!this.canImport()) return;

    const generation = this.generation;
    this.phase.set('importing');
    this.importError.set(null);
    try {
      const result = await firstValueFrom(
        this.shop.importItems({ csv: this.csv, fileName: this.sentFileName }, this.idempotencyKey),
      );
      if (generation !== this.generation) return;
      this.result.set(result);
      this.preview.set(null);
      this.csv = '';
      this.phase.set('done');
      this.focus('.result');
    } catch (error: unknown) {
      if (generation !== this.generation) return;
      const problem = problemOf(error);

      // The file or the catalogue changed under the preview: read the file again so the table shows why.
      if (
        problem !== null &&
        (problem.status === 400 ||
          problem.code === SHOP_CODES.skuTaken ||
          problem.code === SHOP_CODES.slugTaken)
      ) {
        this.importError.set(
          problem.status === 400
            ? 'Nothing was imported: some rows need fixing. The table below shows which.'
            : `Nothing was imported: ${problem.detail ?? 'a SKU or web address was taken meanwhile.'} The table below has been checked again.`,
        );
        await this.runPreview(generation);
        return;
      }

      this.importError.set(messageOf(problem, 'Nothing was imported. Try again.', REFUSALS));
      this.phase.set('ready');
      this.focus('.import-error');
    }
  }

  /** Back to the start, for another file. */
  protected reset(): void {
    this.generation++;
    this.clear();
    this.liveMessage.set('');

    // Looked up after the render: from the result the field is not on the page until then.
    afterNextRender(
      () => {
        const input = this.host.nativeElement.querySelector<HTMLInputElement>('input[type="file"]');
        if (!input) return;
        input.value = '';
        input.focus();
      },
      { injector: this.injector },
    );
  }

  protected price(cents: number | null): string {
    return cents === null ? '—' : PRICE.format(cents / 100);
  }

  protected categories(row: ShopImportRow): string {
    return row.categories.length === 0 ? '—' : row.categories.map(categoryLabel).join(', ');
  }

  private async runPreview(generation: number): Promise<void> {
    this.phase.set('checking');
    try {
      const preview = await firstValueFrom(
        this.shop.previewImport({ csv: this.csv, fileName: this.sentFileName }),
      );
      if (generation !== this.generation) return;
      this.preview.set(preview);
      this.phase.set('ready');
      this.liveMessage.set('');
      this.focus(this.importError() ? '.import-error' : '.summary');
    } catch (error: unknown) {
      if (generation !== this.generation) return;
      const problem = problemOf(error);
      const reasons = problem?.fieldErrors['csv'];
      this.preview.set(null);
      this.refuse(
        'This file can’t be imported',
        reasons && reasons.length > 0
          ? reasons
          : [messageOf(problem, 'The file couldn’t be checked. Try again.', REFUSALS)],
      );
    }
  }

  private refuse(heading: string, reasons: readonly string[]): void {
    this.csv = '';
    this.fileProblem.set({ heading, reasons });
    this.phase.set('idle');
    this.liveMessage.set('');
    this.focus('.file-problem');
  }

  private clear(): void {
    this.csv = '';
    this.sentFileName = null;
    this.idempotencyKey = '';
    this.fileName.set(null);
    this.preview.set(null);
    this.result.set(null);
    this.fileProblem.set(null);
    this.importError.set(null);
    this.phase.set('idle');
  }

  /**
   * Moves focus to what just appeared, so a keyboard or screen-reader user lands on the outcome. The outcome is read
   * out by the focus alone; the live region carries only what has no place to focus (checking, the download).
   */
  private focus(selector: string): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus(), {
      injector: this.injector,
    });
  }
}

/**
 * A fresh idempotency key. `crypto.randomUUID` exists only in a secure context, so a CMS opened over plain HTTP (a
 * LAN address in development) falls back to 128 random bits as hex, which the API accepts all the same.
 */
function newIdempotencyKey(): string {
  if (typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  return [...crypto.getRandomValues(new Uint8Array(16))]
    .map((byte) => byte.toString(16).padStart(2, '0'))
    .join('');
}
