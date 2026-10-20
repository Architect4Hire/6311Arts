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
import {
  ARTIST_CODES,
  ARTIST_LIMITS,
  Artist,
  ArtistCommand,
  ArtistCreate,
  ArtistProblem,
  ArtistUpdate,
  STATUS_CHIPS,
  decodeArtistProblem,
  isSlug,
  slugFromName,
} from '../../models/artist';
import { MediaAsset, MediaAssetSummary } from '../../models/media';
import { ROLES } from '../../models/session';
import { ArtistService } from '../../services/artist.service';
import { MediaService } from '../../services/media.service';
import { MediaPicker } from '../media/media-picker';

interface Draft {
  readonly name: string;
  readonly slug: string;
  /** Whether the person has typed a slug; until then it follows the name (new artists only). */
  readonly slugEdited: boolean;
  readonly medium: string;
  readonly bio: string;
  readonly active: boolean;
  readonly portraitMediaId: string | null;
}

/** What the editor shows of the portrait: a thumbnail and whether it has the alt text publishing needs. */
interface Portrait {
  readonly id: string;
  readonly url: string | null;
  readonly altText: string | null;
  /** The image's details couldn't be fetched: its alt text is unknown, not missing. */
  readonly unavailable?: boolean;
}

type Status =
  | { readonly kind: 'loading' }
  | { readonly kind: 'load-failed' }
  | { readonly kind: 'not-found' }
  | { readonly kind: 'ready' }
  | { readonly kind: 'saving' }
  | { readonly kind: 'commanding'; readonly command: ArtistCommand }
  | { readonly kind: 'conflict'; readonly theirs: Artist };

type FieldErrors = Partial<Record<'name' | 'slug' | 'medium' | 'bio' | 'portraitMediaId', string>>;

const EMPTY: Draft = {
  name: '',
  slug: '',
  slugEdited: false,
  medium: '',
  bio: '',
  active: true,
  portraitMediaId: null,
};

/** Router state set by "Create artist", read by the editor it navigates to. */
const CREATED_STATE = 'artistCreated';

const COMMAND_LABELS: Readonly<
  Record<ArtistCommand, { readonly busy: string; readonly done: string }>
> = {
  publish: { busy: 'Publishing…', done: 'Published.' },
  unpublish: { busy: 'Unpublishing…', done: 'Unpublished. The artist is a draft again.' },
  archive: { busy: 'Archiving…', done: 'Archived.' },
};

/**
 * One artist (`/admin/artists/new`, `/admin/artists/:id`): name, web address, medium, bio, whether they show here
 * now, and their portrait from the media library; then publish, unpublish or (Admins) archive.
 *
 * Unsaved edits survive every recoverable failure (frontend.md): a refused save keeps the form as typed, and a 409
 * shows the saved version beside the editor's own so neither is lost. Status commands act on the saved version, so
 * they wait until the form is saved.
 */
@Component({
  selector: 'cms-artist-editor',
  imports: [
    FormsModule,
    RouterLink,
    MediaPicker,
    UiButton,
    UiControl,
    UiField,
    UiNotice,
    UiStatusChip,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p class="back"><a routerLink="/artists">← All artists</a></p>

    <!-- Exists from the start, so what is written into it later is read out. -->
    <p class="sr-only" aria-live="polite">{{ liveMessage() }}</p>

    @switch (status().kind) {
      @case ('loading') {
        <p role="status">Loading artist…</p>
      }
      @case ('load-failed') {
        <ui-notice tone="error" heading="This artist couldn’t be loaded">
          <p>6311arts may be unreachable for a moment.</p>
          <button uiButton type="button" size="sm" (click)="load()">Try again</button>
        </ui-notice>
      }
      @case ('not-found') {
        <ui-notice tone="warning" heading="There’s no such artist">
          <p>It may have been removed, or the link is wrong.</p>
          <a uiButton size="sm" routerLink="/artists">Back to the artists</a>
        </ui-notice>
      }
      @default {
        <header class="title">
          <h1 #heading class="cms-title" tabindex="-1">{{ artist()?.name || 'New artist' }}</h1>
          @if (artist(); as artist) {
            <ui-status-chip [tone]="chips[artist.status].tone" [icon]="chips[artist.status].icon">{{
              chips[artist.status].label
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
            <ui-notice
              tone="warning"
              heading="Someone else saved this artist while you were editing"
            >
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
                    <th scope="row">Name</th>
                    <td>{{ draft().name || '—' }}</td>
                    <td>{{ theirs.name }}</td>
                  </tr>
                  <tr>
                    <th scope="row">Web address</th>
                    <td>/artists/{{ draft().slug }}</td>
                    <td>/artists/{{ theirs.slug }}</td>
                  </tr>
                  <tr>
                    <th scope="row">Medium</th>
                    <td>{{ draft().medium || '—' }}</td>
                    <td>{{ theirs.medium || '—' }}</td>
                  </tr>
                  <tr>
                    <th scope="row">Bio</th>
                    <td>{{ excerpt(draft().bio) }}</td>
                    <td>{{ excerpt(theirs.bio ?? '') }}</td>
                  </tr>
                  <tr>
                    <th scope="row">Shows here now</th>
                    <td>{{ draft().active ? 'Yes' : 'No' }}</td>
                    <td>{{ theirs.active ? 'Yes' : 'No' }}</td>
                  </tr>
                  <tr>
                    <th scope="row">Portrait</th>
                    <td>{{ portraitWord(draft().portraitMediaId, theirs.portraitMediaId) }}</td>
                    <td>{{ theirs.portraitMediaId ? 'An image' : 'None' }}</td>
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
            <!-- Disabled while a save or command is in flight, so nothing typed then is overwritten by its answer. -->
            <fieldset class="fields" [disabled]="busy()">
              <legend class="sr-only">Artist details</legend>
            <ui-field label="Name" [required]="true" [error]="errors().name ?? null">
              <input
                uiControl
                name="name"
                autocomplete="off"
                [attr.maxlength]="limits.nameMaxLength"
                [ngModel]="draft().name"
                (ngModelChange)="setName($event)"
              />
            </ui-field>

            <ui-field
              label="Web address"
              [required]="!slugLocked()"
              [hint]="slugHint()"
              [error]="errors().slug ?? null"
            >
              <input
                uiControl
                name="slug"
                autocomplete="off"
                spellcheck="false"
                [attr.maxlength]="limits.slugMaxLength"
                [readonly]="slugLocked()"
                [ngModel]="draft().slug"
                (ngModelChange)="setSlug($event)"
              />
            </ui-field>

            <ui-field
              label="Medium"
              hint="What they make, as the site shows it under their name — “Ceramics”, “Illustration & prints”."
              [error]="errors().medium ?? null"
            >
              <input
                uiControl
                name="medium"
                [attr.maxlength]="limits.mediumMaxLength"
                [ngModel]="draft().medium"
                (ngModelChange)="patch({ medium: $event })"
              />
            </ui-field>

            <ui-field
              label="Bio"
              hint="Plain text. Leave a blank line between paragraphs."
              [error]="errors().bio ?? null"
            >
              <textarea
                uiControl
                name="bio"
                rows="8"
                [attr.maxlength]="limits.bioMaxLength"
                [ngModel]="draft().bio"
                (ngModelChange)="patch({ bio: $event })"
              ></textarea>
            </ui-field>

            <label class="check">
              <input
                type="checkbox"
                name="active"
                [checked]="draft().active"
                (change)="patch({ active: activeBox.checked })"
                #activeBox
              />
              <span>
                <span class="check__label">Shows at 6311arts now</span>
                <span class="check__hint"
                  >Turn off for artists who no longer show here. This doesn’t unpublish them.</span
                >
              </span>
            </label>
            </fieldset>

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
              @if (artist() && dirty()) {
                <button uiButton type="button" [disabled]="busy()" (click)="discard()">
                  Discard changes
                </button>
              }
            </div>
          </form>

          <aside class="side">
            <section class="panel" aria-labelledby="portrait-heading">
              <h2 id="portrait-heading" class="panel__title">Portrait</h2>
              @if (portrait()?.unavailable) {
                <ui-notice tone="error">
                  This image’s details couldn’t be loaded.
                  <button uiButton type="button" size="sm" (click)="retryPortrait()">Try again</button>
                </ui-notice>
              } @else if (portrait(); as portrait) {
                @if (portrait.url) {
                  <img class="portrait" [src]="portrait.url" [alt]="portrait.altText ?? ''" />
                } @else {
                  <div class="portrait portrait--pending">Preview not ready yet</div>
                }
                @if (!portrait.altText) {
                  <ui-notice tone="warning">
                    This image has no alt text yet. Add it in Media before publishing the artist.
                  </ui-notice>
                }
              } @else if (draft().portraitMediaId) {
                <p role="status">Loading portrait…</p>
              } @else {
                <p class="panel__empty">No portrait yet.</p>
              }
              @if (errors().portraitMediaId; as message) {
                <p class="error" role="alert">{{ message }}</p>
              }
              <div class="actions">
                <button
                  #portraitButton
                  uiButton
                  type="button"
                  size="sm"
                  [disabled]="busy()"
                  (click)="picking.set(true)"
                >
                  {{ draft().portraitMediaId ? 'Change portrait' : 'Choose portrait' }}
                </button>
                @if (draft().portraitMediaId) {
                  <button
                    uiButton
                    type="button"
                    size="sm"
                    [disabled]="busy()"
                    (click)="removePortrait()"
                  >
                    Remove portrait
                  </button>
                }
              </div>
            </section>

            @if (artist(); as artist) {
              <section class="panel" aria-labelledby="publishing-heading">
                <h2 #publishingHeading id="publishing-heading" class="panel__title" tabindex="-1">
                  Publishing
                </h2>
                <p class="panel__text">{{ statusText(artist) }}</p>
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
                  @if (artist.status === 'Published') {
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
              </section>
            }
          </aside>
        </div>

        <cms-media-picker [(open)]="picking" (picked)="choosePortrait($event)" />
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
    .conflict:focus {
      outline: none;
    }
    .layout {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 300px), 1fr));
      gap: var(--space-6);
      align-items: start;
    }
    .fields {
      display: grid;
      gap: var(--space-4);
      min-width: 0;
      margin: 0;
      padding: 0;
      border: 0;
    }
    .panel__title:focus {
      outline: none;
    }
    .form,
    .side {
      display: grid;
      gap: var(--space-4);
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
    .panel__empty,
    .panel__hint {
      margin: 0;
    }
    .panel__hint,
    .panel__empty,
    .check__hint {
      font-size: var(--fs-small);
      color: var(--text-soft);
    }
    .portrait {
      width: 100%;
      aspect-ratio: 4 / 5;
      object-fit: cover;
      background: var(--light);
    }
    .portrait--pending {
      display: grid;
      place-items: center;
      color: var(--text-soft);
    }
    .error {
      margin: 0;
      color: var(--danger);
      font-weight: var(--fw-bold);
    }
    .check {
      display: flex;
      gap: var(--space-2);
      align-items: flex-start;
      cursor: pointer;
    }
    .check input {
      margin-top: var(--space-1);
      accent-color: var(--ink);
    }
    .check__label,
    .check__hint {
      display: block;
    }
    .check__label {
      font-weight: var(--fw-bold);
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
export class ArtistEditor implements HasUnsavedChanges {
  /** The route's `:id`; absent on `/artists/new`. */
  readonly id = input<string>();

  private readonly artists = inject(ArtistService);
  private readonly media = inject(MediaService);
  private readonly confirm = inject(ConfirmService);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly conflictBox = viewChild<ElementRef<HTMLElement>>('conflictBox');
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly publishingHeading = viewChild<ElementRef<HTMLElement>>('publishingHeading');
  private readonly portraitButton = viewChild<ElementRef<HTMLElement>>('portraitButton');

  /** Arrived straight from "Create artist", which navigated here from `artists/new` (another component). */
  private readonly justCreated =
    this.router.currentNavigation()?.extras.state?.[CREATED_STATE] === true;

  protected readonly chips = STATUS_CHIPS;
  protected readonly limits = ARTIST_LIMITS;

  protected readonly status = signal<Status>({ kind: 'loading' });
  /** The artist as saved; `null` while creating one. */
  protected readonly artist = signal<Artist | null>(null);
  protected readonly draft = signal<Draft>(EMPTY);
  protected readonly portrait = signal<Portrait | null>(null);
  protected readonly errors = signal<FieldErrors>({});
  protected readonly formError = signal<string | null>(null);
  protected readonly liveMessage = signal('');
  protected readonly picking = signal(false);

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
  protected readonly slugLocked = computed(() => this.artist()?.slugLocked ?? false);
  protected readonly canPublish = computed(() => {
    const status = this.artist()?.status;
    return status === 'Draft' || status === 'Scheduled';
  });
  protected readonly canArchive = computed(() => {
    const status = this.artist()?.status;
    return this.isAdmin() && status !== undefined && status !== 'Archived';
  });

  protected readonly saveLabel = computed(() => {
    if (this.status().kind === 'saving') return this.artist() ? 'Saving…' : 'Creating…';
    return this.artist() ? 'Save' : 'Create artist';
  });

  protected readonly slugHint = computed(() =>
    this.slugLocked()
      ? `6311arts.com/artists/${this.draft().slug} — fixed now the artist has been published, so links to it keep working.`
      : `6311arts.com/artists/${this.draft().slug || '…'} — lower-case letters, digits and hyphens.`,
  );

  /** Edits not yet saved: the unsaved-changes guard asks before they are lost. */
  readonly dirty = computed(() => {
    const draft = this.draft();
    const artist = this.artist();
    if (!artist) {
      return (
        draft.name.trim() !== '' ||
        draft.medium.trim() !== '' ||
        draft.bio.trim() !== '' ||
        !draft.active ||
        draft.portraitMediaId !== null
      );
    }
    return Object.keys(changesOf(artist, draft)).length > 0;
  });

  constructor() {
    warnBeforeUnload(() => this.hasUnsavedChanges());

    effect(() => {
      this.id();
      untracked(() => void this.load());
    });

    // The portrait's thumbnail and alt text follow whichever image the draft names.
    effect(() => {
      const id = this.draft().portraitMediaId;
      untracked(() => void this.showPortrait(id));
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
      this.artist.set(null);
      this.draft.set(EMPTY);
      this.status.set({ kind: 'ready' });
      return;
    }

    this.status.set({ kind: 'loading' });
    try {
      const artist = await firstValueFrom(this.artists.get(id));
      this.artist.set(artist);
      this.draft.set(draftOf(artist));
      this.status.set({ kind: 'ready' });
      if (this.justCreated) {
        this.announce('Artist created. Publish it when it’s ready.');
        this.focus(this.heading);
      }
    } catch (error: unknown) {
      this.status.set(
        problemOf(error)?.code === ARTIST_CODES.notFound
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

  protected setName(name: string): void {
    const draft = this.draft();
    // A new artist's address follows its name until someone types one.
    const followsName = !this.artist() && !draft.slugEdited;
    this.patch({ name, ...(followsName ? { slug: slugFromName(name) } : {}) });
  }

  protected setSlug(slug: string): void {
    if (this.slugLocked()) return;
    this.patch({ slug, slugEdited: true });
  }

  protected choosePortrait(choice: MediaAssetSummary): void {
    this.portrait.set({ id: choice.id, url: choice.thumbnailUrl, altText: choice.altText });
    this.patch({ portraitMediaId: choice.id });
  }

  protected removePortrait(): void {
    this.patch({ portraitMediaId: null });
  }

  protected retryPortrait(): void {
    void this.showPortrait(this.draft().portraitMediaId, true);
  }

  protected discard(): void {
    const artist = this.artist();
    if (artist) this.draft.set(draftOf(artist));
    this.errors.set({});
    this.formError.set(null);
  }

  protected portraitWord(mine: string | null, theirs: string | null): string {
    if (!mine) return 'None';
    return mine === theirs ? 'The same image' : 'A different image';
  }

  protected excerpt(text: string): string {
    const flat = text.replace(/\s+/g, ' ').trim();
    if (flat === '') return '—';
    return flat.length > 80 ? `${flat.slice(0, 80)}…` : flat;
  }

  protected statusText(artist: Artist): string {
    switch (artist.status) {
      case 'Published':
        return 'Published: on the public site once the site reads from the CMS.';
      case 'Archived':
        return 'Archived: retired from the site. Archiving can’t be undone here.';
      case 'Scheduled':
        return 'Scheduled to publish.';
      default:
        return artist.publishedAt
          ? 'A draft again, after being published. Its web address stays fixed.'
          : 'A draft: only staff can see it.';
    }
  }

  protected async save(etag?: string): Promise<void> {
    if (this.busy() || !this.dirty()) return;

    const draft = this.draft();
    const local = localErrors(draft, this.slugLocked());
    if (Object.keys(local).length > 0) {
      this.showErrors(local, null);
      return;
    }

    const artist = this.artist();
    this.errors.set({});
    this.formError.set(null);
    this.status.set({ kind: 'saving' });
    try {
      if (!artist) {
        const created = await firstValueFrom(this.artists.create(createOf(draft)));
        this.artist.set(created);
        this.draft.set(draftOf(created));
        this.status.set({ kind: 'ready' });
        // The editor for the new id is a new component; it announces the creation and takes focus.
        await this.router.navigate(['/artists', created.id], {
          replaceUrl: true,
          state: { [CREATED_STATE]: true },
        });
        return;
      }

      const saved = await firstValueFrom(
        this.artists.update(artist.id, changesOf(artist, draft), etag ?? artist.etag),
      );
      this.artist.set(saved);
      this.draft.set(draftOf(saved));
      this.status.set({ kind: 'ready' });
      this.announce('Saved.');
    } catch (error: unknown) {
      await this.saveFailed(error);
    }
  }

  /** Saves the editor's own values over the version someone else saved. */
  protected async keepMine(theirs: Artist): Promise<void> {
    this.artist.set(theirs);
    this.status.set({ kind: 'ready' });
    // The comparison and its buttons are gone now, so focus moves to the title rather than being lost.
    this.focus(this.heading);
    if (!this.dirty()) {
      this.announce('Your changes already match the saved version. Nothing more to save.');
      return;
    }
    await this.save(theirs.etag);
  }

  protected useTheirs(theirs: Artist): void {
    this.artist.set(theirs);
    this.draft.set(draftOf(theirs));
    this.status.set({ kind: 'ready' });
    this.announce('Showing the saved version. Your changes were discarded.');
    this.focus(this.heading);
  }

  protected async unpublish(): Promise<void> {
    const confirmed = await this.confirm.confirm({
      heading: 'Unpublish this artist?',
      message:
        'They go back to being a draft and leave the public site. Their web address stays the same, so publishing again restores it.',
      confirmLabel: 'Unpublish',
      cancelLabel: 'Keep published',
    });
    if (confirmed) await this.run('unpublish');
  }

  protected async archive(): Promise<void> {
    const confirmed = await this.confirm.confirm({
      heading: 'Archive this artist?',
      message:
        'An archived artist is retired from the site, and archiving can’t be undone from the CMS. The archive is recorded under your name.',
      confirmLabel: 'Archive artist',
      cancelLabel: 'Keep them',
    });
    if (confirmed) await this.run('archive');
  }

  protected async run(command: ArtistCommand): Promise<void> {
    const artist = this.artist();
    if (!artist || this.busy() || this.dirty()) return;

    this.errors.set({});
    this.formError.set(null);
    this.status.set({ kind: 'commanding', command });
    this.announce(COMMAND_LABELS[command].busy);
    try {
      const updated = await firstValueFrom(this.artists.command(artist.id, command, artist.etag));
      this.artist.set(updated);
      this.draft.set(draftOf(updated));
      this.status.set({ kind: 'ready' });
      this.announce(COMMAND_LABELS[command].done);
      // The button pressed may be gone (Publish, once published), so focus goes to the panel's heading.
      this.focus(this.publishingHeading);
    } catch (error: unknown) {
      await this.commandFailed(error);
    }
  }

  private async saveFailed(error: unknown): Promise<void> {
    const problem = problemOf(error);

    if (problem?.code === ARTIST_CODES.concurrencyConflict && this.artist()) {
      try {
        const theirs = await firstValueFrom(this.artists.get(this.artist()!.id));
        this.status.set({ kind: 'conflict', theirs });
        this.announce(
          'Someone else saved this artist while you were editing. Compare the two versions.',
        );
        // Focus follows the news, so a keyboard or screen-reader user lands on the comparison.
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

  /**
   * A refused command. The form holds no unsaved edits (commands wait for a save), so the latest saved version is
   * simply shown again with the reason — a conflict or a status someone else changed included.
   */
  private async commandFailed(error: unknown): Promise<void> {
    const problem = problemOf(error);
    const stale =
      problem?.code === ARTIST_CODES.concurrencyConflict ||
      problem?.code === ARTIST_CODES.transitionInvalid;

    if (stale && this.artist()) {
      try {
        const latest = await firstValueFrom(this.artists.get(this.artist()!.id));
        this.artist.set(latest);
        this.draft.set(draftOf(latest));
      } catch {
        // The message below still says what happened.
      }
    }

    this.status.set({ kind: 'ready' });
    this.showErrors(
      fieldErrorsOf(problem),
      problem?.code === ARTIST_CODES.concurrencyConflict
        ? 'Someone else changed this artist a moment ago. Here is the latest version; check it and try again.'
        : messageOf(problem, 'That didn’t work. Try again.'),
    );
  }

  private showErrors(errors: FieldErrors, message: string | null): void {
    this.errors.set(errors);
    this.formError.set(message);
    this.announce(message ?? 'Some fields need attention.');
    // The message is linked to the field and read when it has focus (UiField), so focus goes to the first one. The
    // portrait has no field, so its error sends focus to the button that fixes it.
    afterNextRender(
      () => {
        const invalid = this.host.nativeElement.querySelector<HTMLElement>('[aria-invalid="true"]');
        const target = invalid ?? (errors.portraitMediaId ? this.portraitButton()?.nativeElement : null);
        target?.focus();
      },
      { injector: this.injector },
    );
  }

  /** Writes to the live region. The same words twice still change it, so they are read out again. */
  private announce(message: string): void {
    this.liveMessage.update((current) => (current === message ? `${message}\u00a0` : message));
  }

  private focus(target: () => ElementRef<HTMLElement> | undefined): void {
    afterNextRender(() => target()?.nativeElement.focus(), { injector: this.injector });
  }

  private async showPortrait(id: string | null, retry = false): Promise<void> {
    if (id === null) {
      this.portrait.set(null);
      return;
    }
    if (!retry && this.portrait()?.id === id) return;

    this.portrait.set(null);
    try {
      const asset = await firstValueFrom(this.media.get(id));
      if (this.draft().portraitMediaId === id) this.portrait.set(portraitOf(asset));
    } catch {
      if (this.draft().portraitMediaId === id) {
        this.portrait.set({ id, url: null, altText: null, unavailable: true });
      }
    }
  }
}

function draftOf(artist: Artist): Draft {
  return {
    name: artist.name,
    slug: artist.slug,
    slugEdited: true,
    medium: artist.medium ?? '',
    bio: artist.bio ?? '',
    active: artist.active,
    portraitMediaId: artist.portraitMediaId,
  };
}

function portraitOf(asset: MediaAsset): Portrait {
  const url =
    asset.renditions.find((rendition) => rendition.name === '400.webp')?.url ??
    [...asset.renditions].sort((a, b) => a.width - b.width)[0]?.url ??
    null;
  return { id: asset.id, url, altText: asset.altText };
}

/** Bio line endings as the API stores them, so an untouched bio never looks edited. */
const normalizeBio = (bio: string) => bio.replace(/\r\n?/g, '\n').trim();

function createOf(draft: Draft): ArtistCreate {
  const medium = draft.medium.trim();
  const bio = normalizeBio(draft.bio);
  return {
    name: draft.name.trim(),
    ...(draft.slug ? { slug: draft.slug } : {}),
    ...(medium ? { medium } : {}),
    ...(bio ? { bio } : {}),
    active: draft.active,
    ...(draft.portraitMediaId ? { portraitMediaId: draft.portraitMediaId } : {}),
  };
}

/** Only what changed; an emptied medium or bio is sent as "" so the API clears it. */
function changesOf(artist: Artist, draft: Draft): ArtistUpdate {
  const name = draft.name.trim();
  const medium = draft.medium.trim();
  const bio = normalizeBio(draft.bio);
  return {
    ...(name !== artist.name ? { name } : {}),
    ...(draft.slug !== artist.slug ? { slug: draft.slug } : {}),
    ...(medium !== (artist.medium ?? '') ? { medium } : {}),
    ...(bio !== (artist.bio ?? '') ? { bio } : {}),
    ...(draft.active !== artist.active ? { active: draft.active } : {}),
    ...(draft.portraitMediaId !== artist.portraitMediaId
      ? draft.portraitMediaId
        ? { portraitMediaId: draft.portraitMediaId }
        : { removePortrait: true as const }
      : {}),
  };
}

/** What the form can tell before asking: the API checks the same and has the last word. */
function localErrors(draft: Draft, slugLocked: boolean): FieldErrors {
  const errors: FieldErrors = {};
  if (draft.name.trim() === '') errors.name = 'Enter the artist’s name.';
  if (!slugLocked && !isSlug(draft.slug)) {
    errors.slug = draft.slug
      ? 'Use lower-case letters, digits and single hyphens (for example jane-doe).'
      : 'Enter a web address, for example jane-doe.';
  }
  return errors;
}

function problemOf(error: unknown): ArtistProblem | null {
  return error instanceof HttpErrorResponse ? decodeArtistProblem(error.error, error.status) : null;
}

function fieldErrorsOf(problem: ArtistProblem | null): FieldErrors {
  const errors: FieldErrors = {};
  for (const key of ['name', 'slug', 'medium', 'bio', 'portraitMediaId'] as const) {
    const message = problem?.fieldErrors[key]?.[0];
    if (message) errors[key] = message;
  }
  return errors;
}

function messageOf(problem: ArtistProblem | null, fallback: string): string {
  if (problem === null || problem.status === 0) {
    return 'Can’t reach 6311arts. Your changes are still here; try again.';
  }
  return problem.detail ?? fallback;
}
