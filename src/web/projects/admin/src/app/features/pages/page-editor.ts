import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiControl, UiField, UiNotice, UiStatusChip } from '@6311/ui';
import { ConfirmService } from '../../core/confirm/confirm.service';
import { HasUnsavedChanges, warnBeforeUnload } from '../../core/guards/unsaved-changes.guard';
import { SessionService } from '../../core/session/session.service';
import { STATUS_CHIPS, isSlug, slugFromName } from '../../models/content';
import {
  PAGE_CODES,
  PAGE_LIMITS,
  Page,
  PageCommand,
  PageCreate,
  PageProblem,
  PageUpdate,
  decodePageProblem,
} from '../../models/page';
import { ROLES } from '../../models/session';
import { PageService } from '../../services/page.service';
import { RichTextEditor } from '../../shared/rich-text-editor';

interface Draft {
  readonly title: string;
  readonly slug: string;
  /** Whether the person has typed a slug; until then it follows the title (new pages only). */
  readonly slugEdited: boolean;
  /** HTML, as the rich-text editor holds it. */
  readonly body: string;
}

type Status =
  | { readonly kind: 'loading' }
  | { readonly kind: 'load-failed' }
  | { readonly kind: 'not-found' }
  | { readonly kind: 'ready' }
  | { readonly kind: 'saving' }
  | { readonly kind: 'commanding'; readonly command: PageCommand }
  | { readonly kind: 'conflict'; readonly theirs: Page };

type FieldErrors = Partial<Record<'title' | 'slug' | 'body', string>>;

const EMPTY: Draft = { title: '', slug: '', slugEdited: false, body: '' };

/** Router state set by "Create page", read by the editor it navigates to. */
const CREATED_STATE = 'pageCreated';

const COMMAND_LABELS: Readonly<
  Record<PageCommand, { readonly busy: string; readonly done: string }>
> = {
  publish: { busy: 'Publishing…', done: 'Published.' },
  unpublish: { busy: 'Unpublishing…', done: 'Unpublished. The page is a draft again.' },
  archive: { busy: 'Archiving…', done: 'Archived.' },
  schedule: { busy: 'Scheduling…', done: 'Scheduled.' },
  unschedule: { busy: 'Unscheduling…', done: 'Unscheduled. The page is a draft again.' },
};

/**
 * One page (`/admin/pages/new`, `/admin/pages/:id`): title, web address and a rich-text body; then publish,
 * unpublish or (Admins) archive.
 *
 * The body is edited with `<cms-rich-text>`, whose schema is the server's allow-list (ADR 0034); the API sanitizes it
 * regardless and answers with what it stored, which the form then shows. Unsaved edits survive every recoverable
 * failure, and a 409 shows the saved version beside the editor's own (frontend.md). Status commands act on the saved
 * version, so they wait until the form is saved.
 */
import { PreviewLinkButton } from '../../shared/preview-link';
import { ScheduleControl } from '../../shared/schedule-control';

@Component({
  selector: 'cms-page-editor',
  imports: [
    FormsModule,
    RouterLink,
    RichTextEditor,
    UiButton,
    UiControl,
    UiField,
    UiNotice,
    UiStatusChip,
    PreviewLinkButton,
    ScheduleControl,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p class="back"><a routerLink="/pages">← All pages</a></p>

    <!-- Exists from the start, so what is written into it later is read out. -->
    <p class="sr-only" aria-live="polite">{{ liveMessage() }}</p>

    @switch (status().kind) {
      @case ('loading') {
        <p role="status">Loading page…</p>
      }
      @case ('load-failed') {
        <ui-notice tone="error" heading="This page couldn’t be loaded">
          <p>6311arts may be unreachable for a moment.</p>
          <button uiButton type="button" size="sm" (click)="load()">Try again</button>
        </ui-notice>
      }
      @case ('not-found') {
        <ui-notice tone="warning" heading="There’s no such page">
          <p>It may have been removed, or the link is wrong.</p>
          <a uiButton size="sm" routerLink="/pages">Back to the pages</a>
        </ui-notice>
      }
      @default {
        <header class="title">
          <h1 #heading class="cms-title" tabindex="-1">{{ page()?.title || 'New page' }}</h1>
          @if (page(); as page) {
            <ui-status-chip [tone]="chips[page.status].tone" [icon]="chips[page.status].icon">{{
              chips[page.status].label
            }}</ui-status-chip>
          }
        </header>

        @if (conflict(); as theirs) {
          <div
            #conflictBox
            class="conflict"
            role="region"
            aria-label="Your changes and the saved version"
            tabindex="-1"
          >
            <ui-notice tone="warning" heading="Someone else saved this page while you were editing">
              <table class="compare">
                <caption class="sr-only">
                  Your changes and the saved version
                </caption>
                <thead>
                  <tr>
                    <th scope="col">Field</th>
                    <th scope="col">Yours</th>
                    <th scope="col">Saved</th>
                  </tr>
                </thead>
                <tbody>
                  <tr>
                    <th scope="row">Title</th>
                    <td>{{ draft().title || '—' }}</td>
                    <td>{{ theirs.title }}</td>
                  </tr>
                  <tr>
                    <th scope="row">Web address</th>
                    <td>/{{ draft().slug }}</td>
                    <td>/{{ theirs.slug }}</td>
                  </tr>
                  <tr>
                    <th scope="row">Text</th>
                    <td>{{ excerpt(draft().body) }}</td>
                    <td>{{ excerpt(theirs.body ?? '') }}</td>
                  </tr>
                </tbody>
              </table>
              <div class="actions">
                <button uiButton type="button" variant="dark" size="sm" (click)="keepMine(theirs)">
                  Keep my changes
                </button>
                <button uiButton type="button" size="sm" (click)="useTheirs(theirs)">
                  Use the saved version and discard mine
                </button>
              </div>
            </ui-notice>
          </div>
        }

        @if (formError(); as message) {
          <ui-notice tone="error">{{ message }}</ui-notice>
        }

        <div class="layout">
          <form class="form" novalidate (ngSubmit)="save()">
            <fieldset class="fields" [disabled]="busy()">
              <legend class="sr-only">Page details</legend>
              <ui-field label="Title" [required]="true" [error]="errors().title ?? null">
                <input
                  uiControl
                  name="title"
                  autocomplete="off"
                  [attr.maxlength]="limits.titleMaxLength"
                  [ngModel]="draft().title"
                  (ngModelChange)="setTitle($event)"
                />
              </ui-field>

              <ui-field
                label="Web address"
                [required]="true"
                [hint]="slugHint()"
                [error]="errors().slug ?? null"
              >
                <input
                  uiControl
                  name="slug"
                  autocomplete="off"
                  spellcheck="false"
                  [attr.maxlength]="limits.slugMaxLength"
                  [ngModel]="draft().slug"
                  (ngModelChange)="setSlug($event)"
                />
              </ui-field>
            </fieldset>

            <cms-rich-text
              label="Text"
              hint="Headings, lists, links and pictures from the media library. Needed to publish."
              [value]="draft().body"
              (valueChange)="setBody($event)"
              [disabled]="busy()"
              [error]="errors().body ?? null"
            />

            <div class="actions">
              <button
                uiButton
                type="submit"
                variant="dark"
                [busy]="status().kind === 'saving'"
                [disabled]="!dirty() || busy()"
              >
                {{ saveLabel() }}
              </button>
              @if (page() && dirty()) {
                <button uiButton type="button" [disabled]="busy()" (click)="discard()">
                  Discard changes
                </button>
              }
            </div>
          </form>

          @if (page(); as page) {
            <aside class="side">
              <section class="panel" aria-labelledby="publishing-heading">
                <h2 #publishingHeading id="publishing-heading" class="panel__title" tabindex="-1">
                  Publishing
                </h2>
                <p class="panel__text">{{ statusText(page) }}</p>
                @if (dirty()) {
                  <p class="panel__hint" id="publishing-hint">
                    Save your changes first: these act on the saved version.
                  </p>
                }
                <div class="actions">
                  @if (canPublish()) {
                    <button
                      uiButton
                      type="button"
                      variant="dark"
                      size="sm"
                      aria-describedby="publishing-hint"
                      [busy]="commanding() === 'publish'"
                      [disabled]="dirty() || busy()"
                      (click)="run('publish')"
                    >
                      Publish
                    </button>
                  }
                  @if (page.status === 'Published') {
                    <button
                      uiButton
                      type="button"
                      size="sm"
                      aria-describedby="publishing-hint"
                      [busy]="commanding() === 'unpublish'"
                      [disabled]="dirty() || busy()"
                      (click)="unpublish()"
                    >
                      Unpublish
                    </button>
                  }
                  @if (canArchive()) {
                    <button
                      uiButton
                      type="button"
                      size="sm"
                      aria-describedby="publishing-hint"
                      [busy]="commanding() === 'archive'"
                      [disabled]="dirty() || busy()"
                      (click)="archive()"
                    >
                      Archive
                    </button>
                  }
                </div>

                <cms-schedule-control
                  noun="page"
                  [status]="page.status"
                  [scheduledFor]="page.scheduledFor"
                  [disabled]="dirty() || busy()"
                  [busy]="commanding()"
                  (schedule)="schedule($event)"
                  (unschedule)="unschedule()"
                />

                <cms-preview-link entityType="page" [entityId]="page.id" [dirty]="dirty()" />
              </section>
            </aside>
          }
        </div>
      }
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-5);
    }
    .back {
      margin: 0;
      font-size: var(--fs-small);
    }
    .title {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3);
      align-items: center;
    }
    .title .cms-title {
      margin: 0;
    }
    .cms-title:focus,
    .conflict:focus,
    .panel__title:focus {
      outline: none;
    }
    .layout {
      display: grid;
      grid-template-columns: minmax(0, 3fr) minmax(min(100%, 240px), 1fr);
      gap: var(--space-6);
      align-items: start;
    }
    @media (max-width: 900px) {
      .layout {
        grid-template-columns: minmax(0, 1fr);
      }
    }
    .form,
    .side,
    .fields {
      display: grid;
      gap: var(--space-4);
      min-width: 0;
    }
    .fields {
      margin: 0;
      padding: 0;
      border: 0;
    }
    .panel {
      display: grid;
      gap: var(--space-3);
      padding: var(--space-4);
      border: var(--border-thin) solid var(--line);
    }
    .panel__title {
      margin: 0;
      font-size: var(--fs-label);
      font-weight: var(--fw-heavy);
      letter-spacing: var(--track-label);
      text-transform: uppercase;
    }
    .panel__text,
    .panel__hint {
      margin: 0;
    }
    .panel__hint {
      font-size: var(--fs-small);
      color: var(--text-soft);
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
    .compare {
      width: 100%;
      border-collapse: collapse;
      font-size: var(--fs-small);
    }
    .compare th,
    .compare td {
      padding: var(--space-1) var(--space-2);
      border-bottom: var(--border-thin) solid var(--line);
      text-align: left;
      vertical-align: top;
      overflow-wrap: anywhere;
    }
  `,
})
export class PageEditor implements HasUnsavedChanges {
  /** The route's `:id`; absent on `/pages/new`. */
  readonly id = input<string>();

  private readonly pages = inject(PageService);
  private readonly confirm = inject(ConfirmService);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly conflictBox = viewChild<ElementRef<HTMLElement>>('conflictBox');
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly publishingHeading = viewChild<ElementRef<HTMLElement>>('publishingHeading');
  private readonly body = viewChild(RichTextEditor);

  private readonly justCreated =
    this.router.currentNavigation()?.extras.state?.[CREATED_STATE] === true;

  protected readonly chips = STATUS_CHIPS;
  protected readonly limits = PAGE_LIMITS;

  protected readonly status = signal<Status>({ kind: 'loading' });
  /** The page as saved; `null` while creating one. */
  protected readonly page = signal<Page | null>(null);
  protected readonly draft = signal<Draft>(EMPTY);
  protected readonly errors = signal<FieldErrors>({});
  protected readonly formError = signal<string | null>(null);
  protected readonly liveMessage = signal('');

  protected readonly isAdmin = computed(() => this.session.hasAnyRole([ROLES.admin]));
  protected readonly busy = computed(() => {
    const kind = this.status().kind;
    return kind === 'saving' || kind === 'commanding';
  });
  protected readonly commanding = computed(() => {
    const status = this.status();
    return status.kind === 'commanding' ? status.command : null;
  });
  protected readonly conflict = computed(() => {
    const status = this.status();
    return status.kind === 'conflict' ? status.theirs : null;
  });
  protected readonly slugLocked = computed(() => this.page()?.slugLocked ?? false);
  protected readonly canPublish = computed(() => {
    const status = this.page()?.status;
    return status === 'Draft' || status === 'Scheduled';
  });
  protected readonly canArchive = computed(() => {
    const status = this.page()?.status;
    return this.isAdmin() && status !== undefined && status !== 'Archived';
  });

  protected readonly saveLabel = computed(() => {
    if (this.status().kind === 'saving') return this.page() ? 'Saving…' : 'Creating…';
    return this.page() ? 'Save' : 'Create page';
  });

  /**
   * Once published, the address is someone's bookmark: changing it is allowed, and the old address is redirected to the
   * new one when it is saved (ADR 0035). The hint says so, and names the address that will redirect.
   */
  protected readonly slugHint = computed(() => {
    const saved = this.page();
    const slug = this.draft().slug;
    if (!this.slugLocked() || !saved) return `6311arts.com/${this.draft().slug || '…'} — lower-case letters, digits and hyphens.`;
    return slug === saved.slug
      ? `6311arts.com/${slug} — published, so changing it sends visitors from this address to the new one.`
      : `6311arts.com/${slug || '…'} — on saving, visitors to /${saved.slug} are sent here.`;
  });

  /** Edits not yet saved: the unsaved-changes guard asks before they are lost. */
  readonly dirty = computed(() => {
    const draft = this.draft();
    const page = this.page();
    if (!page) return draft.title.trim() !== '' || draft.body !== '';
    return Object.keys(changesOf(page, draft)).length > 0;
  });

  constructor() {
    warnBeforeUnload(() => this.hasUnsavedChanges());

    effect(() => {
      this.id();
      untracked(() => void this.load());
    });
  }

  hasUnsavedChanges(): boolean {
    return this.dirty() || this.busy();
  }

  async load(): Promise<void> {
    const id = this.id();
    this.errors.set({});
    this.formError.set(null);

    if (!id) {
      this.page.set(null);
      this.draft.set(EMPTY);
      this.status.set({ kind: 'ready' });
      return;
    }

    this.status.set({ kind: 'loading' });
    try {
      const page = await firstValueFrom(this.pages.get(id));
      this.page.set(page);
      this.draft.set(draftOf(page));
      this.status.set({ kind: 'ready' });
      if (this.justCreated) {
        this.announce('Page created. Publish it when it’s ready.');
        this.focus(this.heading);
      }
    } catch (error: unknown) {
      this.status.set(
        problemOf(error)?.code === PAGE_CODES.notFound
          ? { kind: 'not-found' }
          : { kind: 'load-failed' },
      );
    }
  }

  protected patch(change: Partial<Draft>): void {
    this.draft.update((draft) => ({ ...draft, ...change }));
    const cleared = Object.keys(change).filter((key) => key in this.errors());
    if (cleared.length > 0) {
      this.errors.update((errors) =>
        Object.fromEntries(Object.entries(errors).filter(([key]) => !cleared.includes(key))),
      );
    }
  }

  protected setTitle(title: string): void {
    const followsTitle = !this.page() && !this.draft().slugEdited;
    this.patch({ title, ...(followsTitle ? { slug: slugFromName(title) } : {}) });
  }

  protected setSlug(slug: string): void {
    this.patch({ slug, slugEdited: true });
  }

  protected setBody(body: string): void {
    this.patch({ body });
  }

  protected discard(): void {
    const page = this.page();
    if (page) this.draft.set(draftOf(page));
    this.errors.set({});
    this.formError.set(null);
  }

  /** The words of an HTML body, for the comparison. Parsed inert: a template's content runs and loads nothing. */
  protected excerpt(html: string): string {
    const template = document.createElement('template');
    template.innerHTML = html;
    const flat = (template.content.textContent ?? '').replace(/\s+/g, ' ').trim();
    if (flat === '') return '—';
    return flat.length > 120 ? `${flat.slice(0, 120)}…` : flat;
  }

  protected statusText(page: Page): string {
    switch (page.status) {
      case 'Published':
        return 'Published: on the public site once the site reads from the CMS.';
      case 'Archived':
        return 'Archived: retired from the site. Archiving can’t be undone here.';
      case 'Scheduled':
        return 'Scheduled to publish.';
      default:
        return page.publishedAt
          ? 'A draft again, after being published. Its web address stays fixed.'
          : 'A draft: only staff can see it. Publishing needs some text.';
    }
  }

  protected async save(etag?: string): Promise<void> {
    if (this.busy() || !this.dirty()) return;

    const draft = this.draft();
    const local = localErrors(draft);
    if (Object.keys(local).length > 0) {
      this.showErrors(local, null);
      return;
    }

    const page = this.page();
    this.errors.set({});
    this.formError.set(null);
    this.status.set({ kind: 'saving' });
    try {
      if (!page) {
        const created = await firstValueFrom(this.pages.create(createOf(draft)));
        this.page.set(created);
        this.draft.set(draftOf(created));
        this.status.set({ kind: 'ready' });
        await this.router.navigate(['/pages', created.id], {
          replaceUrl: true,
          state: { [CREATED_STATE]: true },
        });
        return;
      }

      const saved = await firstValueFrom(
        this.pages.update(page.id, changesOf(page, draft), etag ?? page.etag),
      );
      this.page.set(saved);
      this.draft.set(draftOf(saved));
      this.status.set({ kind: 'ready' });
      // Saving disabled the fieldset that held the focused control, so the browser dropped focus to <body>.
      // Put it on the heading, which is where "Saved." is announced from, rather than leaving a keyboard or
      // screen-reader user with no position on the page (WCAG 2.4.3 focus order).
      this.focus(this.heading);
      this.announce('Saved.');
    } catch (error: unknown) {
      await this.saveFailed(error);
    }
  }

  protected async keepMine(theirs: Page): Promise<void> {
    this.page.set(theirs);
    this.status.set({ kind: 'ready' });
    this.focus(this.heading);
    if (!this.dirty()) {
      this.announce('Your changes already match the saved version. Nothing more to save.');
      return;
    }
    await this.save(theirs.etag);
  }

  protected useTheirs(theirs: Page): void {
    this.page.set(theirs);
    this.draft.set(draftOf(theirs));
    this.status.set({ kind: 'ready' });
    this.announce('Showing the saved version. Your changes were discarded.');
    this.focus(this.heading);
  }

  protected async unpublish(): Promise<void> {
    const confirmed = await this.confirm.confirm({
      heading: 'Unpublish this page?',
      message:
        'It goes back to being a draft and leaves the public site. Its web address stays the same, so publishing again restores it.',
      confirmLabel: 'Unpublish',
      cancelLabel: 'Keep published',
    });
    if (confirmed) await this.run('unpublish');
  }

  protected async archive(): Promise<void> {
    const confirmed = await this.confirm.confirm({
      heading: 'Archive this page?',
      message:
        'An archived page is retired from the site, and archiving can’t be undone from the CMS. The archive is recorded under your name.',
      confirmLabel: 'Archive page',
      cancelLabel: 'Keep it',
    });
    if (confirmed) await this.run('archive');
  }

  /** The `schedule` command: the only one with a body, so it does not go through {@link run} (ADR 0036). */
  protected async schedule(scheduledFor: string): Promise<void> {
    const page = this.page();
    if (!page || this.busy() || this.dirty()) return;

    this.errors.set({});
    this.formError.set(null);
    this.status.set({ kind: 'commanding', command: 'schedule' });
    this.announce(COMMAND_LABELS.schedule.busy);
    try {
      const updated = await firstValueFrom(
        this.pages.schedule(page.id, scheduledFor, page.etag),
      );
      this.page.set(updated);
      this.draft.set(draftOf(updated));
      this.status.set({ kind: 'ready' });
      this.announce(COMMAND_LABELS.schedule.done);
    } catch (error: unknown) {
      await this.commandFailed(error);
    }
  }

  protected async unschedule(): Promise<void> {
    await this.run('unschedule');
  }

  protected async run(command: PageCommand): Promise<void> {
    const page = this.page();
    if (!page || this.busy() || this.dirty()) return;

    this.errors.set({});
    this.formError.set(null);
    this.status.set({ kind: 'commanding', command });
    this.announce(COMMAND_LABELS[command].busy);
    try {
      const updated = await firstValueFrom(this.pages.command(page.id, command, page.etag));
      this.page.set(updated);
      this.draft.set(draftOf(updated));
      this.status.set({ kind: 'ready' });
      this.announce(COMMAND_LABELS[command].done);
      this.focus(this.publishingHeading);
    } catch (error: unknown) {
      await this.commandFailed(error);
    }
  }

  private async saveFailed(error: unknown): Promise<void> {
    const problem = problemOf(error);

    if (problem?.code === PAGE_CODES.concurrencyConflict && this.page()) {
      try {
        const theirs = await firstValueFrom(this.pages.get(this.page()!.id));
        this.status.set({ kind: 'conflict', theirs });
        this.announce(
          'Someone else saved this page while you were editing. Compare the two versions.',
        );
        afterNextRender(() => this.conflictBox()?.nativeElement.focus(), {
          injector: this.injector,
        });
        return;
      } catch {
        // Fall through to the plain message; the draft is still in the form.
      }
    }

    this.status.set({ kind: 'ready' });
    this.showErrors(
      fieldErrorsOf(problem),
      messageOf(problem, 'The changes couldn’t be saved. Try again.'),
    );
  }

  private async commandFailed(error: unknown): Promise<void> {
    const problem = problemOf(error);
    const stale =
      problem?.code === PAGE_CODES.concurrencyConflict ||
      problem?.code === PAGE_CODES.transitionInvalid;

    if (stale && this.page()) {
      try {
        const latest = await firstValueFrom(this.pages.get(this.page()!.id));
        this.page.set(latest);
        this.draft.set(draftOf(latest));
      } catch {
        // The message below still says what happened.
      }
    }

    this.status.set({ kind: 'ready' });
    this.showErrors(
      fieldErrorsOf(problem),
      problem?.code === PAGE_CODES.concurrencyConflict
        ? 'Someone else changed this page a moment ago. Here is the latest version; check it and try again.'
        : messageOf(problem, 'That didn’t work. Try again.'),
    );
  }

  private showErrors(errors: FieldErrors, message: string | null): void {
    this.errors.set(errors);
    this.formError.set(message);
    this.announce(message ?? 'Some fields need attention.');
    // The first invalid field takes focus, so its message is read; the body's is the rich text's own.
    afterNextRender(
      () => {
        const invalid = this.host.nativeElement.querySelector<HTMLElement>(
          'input[aria-invalid="true"]',
        );
        if (invalid) invalid.focus();
        else if (errors.body) this.body()?.focus();
      },
      { injector: this.injector },
    );
  }

  private announce(message: string): void {
    this.liveMessage.update((current) => (current === message ? `${message} ` : message));
  }

  private focus(target: () => ElementRef<HTMLElement> | undefined): void {
    afterNextRender(() => target()?.nativeElement.focus(), { injector: this.injector });
  }
}

function draftOf(page: Page): Draft {
  return { title: page.title, slug: page.slug, slugEdited: true, body: page.body ?? '' };
}

function createOf(draft: Draft): PageCreate {
  return {
    title: draft.title.trim(),
    ...(draft.slug ? { slug: draft.slug } : {}),
    ...(draft.body ? { body: draft.body } : {}),
  };
}

/** Only what changed; an emptied body is sent as "" so the API clears it. */
function changesOf(page: Page, draft: Draft): PageUpdate {
  const title = draft.title.trim();
  return {
    ...(title !== page.title ? { title } : {}),
    ...(draft.slug !== page.slug ? { slug: draft.slug } : {}),
    ...(draft.body !== (page.body ?? '') ? { body: draft.body } : {}),
  };
}

function localErrors(draft: Draft): FieldErrors {
  const errors: FieldErrors = {};
  if (draft.title.trim() === '') errors.title = 'Enter the page’s title.';
  if (!isSlug(draft.slug)) {
    errors.slug = draft.slug
      ? 'Use lower-case letters, digits and single hyphens (for example privacy).'
      : 'Enter a web address, for example privacy.';
  }
  return errors;
}

function problemOf(error: unknown): PageProblem | null {
  return error instanceof HttpErrorResponse ? decodePageProblem(error.error, error.status) : null;
}

function fieldErrorsOf(problem: PageProblem | null): FieldErrors {
  const errors: FieldErrors = {};
  for (const key of ['title', 'slug', 'body'] as const) {
    const message = problem?.fieldErrors[key]?.[0];
    if (message) errors[key] = message;
  }
  return errors;
}

function messageOf(problem: PageProblem | null, fallback: string): string {
  if (problem === null || problem.status === 0)
    return 'Can’t reach 6311arts. Your changes are still here; try again.';
  const message = problem.detail ?? fallback;

  // A 5xx says nothing the editor can act on, so hand them the one thing support can: the correlation id,
  // which is this request's trace id (ADR 0019). Appended here rather than left to the global problem toast,
  // because these calls deliberately suppress it to show the failure in place.
  return problem.status >= 500 && problem.correlationId !== null
    ? `${message} Quote reference ${problem.correlationId} if you report it.`
    : message;
}
