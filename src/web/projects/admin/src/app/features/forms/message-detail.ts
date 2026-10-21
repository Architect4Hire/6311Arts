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
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiNotice, UiStatusChip } from '@6311/ui';
import { HasUnsavedChanges, warnBeforeUnload } from '../../core/guards/unsaved-changes.guard';
import {
  ContactMessage,
  FORMS_CODES,
  FormsProblem,
  HANDLED_CHIPS,
  decodeFormsProblem,
} from '../../models/forms';
import { FormsService } from '../../services/forms.service';
import { formatInstant } from './forms-tabs';

type Status =
  | { readonly kind: 'loading' }
  | { readonly kind: 'load-failed' }
  | { readonly kind: 'not-found' }
  | { readonly kind: 'ready' }
  | { readonly kind: 'saving' };

const EMAIL = /^[^\s@<>]+@[^\s@<>]+$/;

/**
 * One contact message (`/admin/forms/messages/:id`): who wrote, what they said, and whether staff have dealt with
 * it. The message is a visitor's words, so it is bound as text with its line breaks kept, never as HTML. Marking
 * it is one change under the row version this screen read; if someone else marked it first, the screen shows
 * where it now stands rather than overwriting them.
 */
@Component({
  selector: 'cms-message-detail',
  imports: [RouterLink, UiButton, UiNotice, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p class="back"><a routerLink="/forms/messages">← All messages</a></p>

    <p class="sr-only" aria-live="polite">{{ liveMessage() }}</p>

    @switch (status().kind) {
      @case ('loading') {
        <p role="status">Loading message…</p>
      }
      @case ('load-failed') {
        <ui-notice tone="error" heading="This message couldn’t be loaded">
          <p>6311arts may be unreachable for a moment.</p>
          <button uiButton type="button" size="sm" (click)="load()">Try again</button>
        </ui-notice>
      }
      @case ('not-found') {
        <ui-notice tone="warning" heading="There’s no such message">
          <p>The link may be wrong.</p>
          <a uiButton size="sm" routerLink="/forms/messages">Back to the messages</a>
        </ui-notice>
      }
      @default {
        @if (message(); as message) {
          <header class="title">
            <h1 #heading class="cms-title" tabindex="-1">{{ message.name }}</h1>
            <ui-status-chip [tone]="chip().tone" [icon]="chip().icon">{{
              chip().label
            }}</ui-status-chip>
          </header>

          @if (notice(); as notice) {
            <ui-notice [tone]="notice.tone">{{ notice.text }}</ui-notice>
          }

          <dl class="pairs">
            <div class="pair">
              <dt>Email</dt>
              <dd>
                @if (mailto(); as href) {
                  <a [href]="href">{{ message.email }}</a>
                } @else {
                  {{ message.email }}
                }
              </dd>
            </div>
            <div class="pair">
              <dt>Received</dt>
              <dd>
                <time [attr.datetime]="message.receivedAt">{{ instant(message.receivedAt) }}</time>
              </dd>
            </div>
          </dl>

          <section class="body" aria-labelledby="message-heading">
            <h2 class="body__heading" id="message-heading">Message</h2>
            <p class="body__text">{{ message.message }}</p>
          </section>

          <div class="actions">
            <button
              uiButton
              type="button"
              variant="dark"
              [busy]="status().kind === 'saving'"
              [disabled]="status().kind === 'saving'"
              (click)="toggle()"
            >
              {{ message.handled ? 'Mark not handled' : 'Mark handled' }}
            </button>
          </div>
        }
      }
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-5);
      max-width: 720px;
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
      overflow-wrap: anywhere;
    }
    .cms-title:focus {
      outline: none;
    }
    .pairs {
      display: grid;
      gap: var(--space-2);
      margin: 0;
    }
    .pair {
      display: grid;
      gap: var(--space-1) var(--space-4);
    }
    @media (min-width: 600px) {
      .pair {
        grid-template-columns: minmax(100px, 160px) minmax(0, 1fr);
      }
    }
    .pair dt {
      color: var(--muted);
      font-size: var(--fs-small);
    }
    .pair dd {
      margin: 0;
      overflow-wrap: anywhere;
    }
    .body {
      display: grid;
      gap: var(--space-3);
    }
    .body__heading {
      margin: 0;
      padding-bottom: var(--space-2);
      border-bottom: var(--border-thin) solid var(--line);
      font-size: var(--fs-label);
      font-weight: var(--fw-heavy);
      letter-spacing: var(--track-label);
      text-transform: uppercase;
    }
    .body__text {
      margin: 0;
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
  `,
})
export class MessageDetail implements HasUnsavedChanges {
  /** The route's `:id`. */
  readonly id = input.required<string>();

  private readonly forms = inject(FormsService);
  private readonly injector = inject(Injector);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  protected readonly status = signal<Status>({ kind: 'loading' });
  protected readonly message = signal<ContactMessage | null>(null);
  protected readonly notice = signal<{
    readonly tone: 'error' | 'warning';
    readonly text: string;
  } | null>(null);
  protected readonly liveMessage = signal('');

  protected readonly chip = computed(
    () => HANDLED_CHIPS[this.message()?.handled ? 'handled' : 'unhandled'],
  );
  protected readonly mailto = computed(() => {
    const email = this.message()?.email;
    return email && EMAIL.test(email) ? `mailto:${email}` : null;
  });

  constructor() {
    warnBeforeUnload(() => this.hasUnsavedChanges());
    effect(() => {
      this.id();
      untracked(() => void this.load());
    });
  }

  /** Nothing is typed here; only a change still on its way would be lost. */
  hasUnsavedChanges(): boolean {
    return this.status().kind === 'saving';
  }

  async load(): Promise<void> {
    this.notice.set(null);
    this.status.set({ kind: 'loading' });
    try {
      this.message.set(await firstValueFrom(this.forms.getMessage(this.id())));
      this.status.set({ kind: 'ready' });
    } catch (error: unknown) {
      this.status.set(
        problemOf(error)?.code === FORMS_CODES.notFound
          ? { kind: 'not-found' }
          : { kind: 'load-failed' },
      );
    }
  }

  protected instant(iso: string): string {
    return formatInstant(iso);
  }

  protected async toggle(): Promise<void> {
    const message = this.message();
    if (!message || this.status().kind === 'saving') return;

    const handled = !message.handled;
    this.notice.set(null);
    this.status.set({ kind: 'saving' });
    try {
      const saved = await firstValueFrom(
        this.forms.updateMessage(message.id, { handled }, message.etag),
      );
      this.message.set(saved);
      this.status.set({ kind: 'ready' });
      this.announce(saved.handled ? 'Marked handled.' : 'Marked not handled.');
    } catch (error: unknown) {
      await this.toggleFailed(error, message);
    }
  }

  private async toggleFailed(error: unknown, mine: ContactMessage): Promise<void> {
    const problem = problemOf(error);

    if (problem?.code === FORMS_CODES.concurrencyConflict) {
      try {
        const theirs = await firstValueFrom(this.forms.getMessage(mine.id));
        this.message.set(theirs);
        this.status.set({ kind: 'ready' });
        this.show(
          'warning',
          `Someone else changed this message a moment ago. It is now marked ${
            theirs.handled ? 'handled' : 'not handled'
          }; nothing of yours was saved.`,
        );
        return;
      } catch {
        // Fall through to the plain message.
      }
    }

    this.status.set({ kind: 'ready' });
    this.show('error', messageOf(problem, 'The message couldn’t be marked. Try again.'));
  }

  private show(tone: 'error' | 'warning', text: string): void {
    // The notice announces itself (an alert); saying it again through the live region would read it twice.
    this.notice.set({ tone, text });
    // The button that had focus was disabled while saving; keep the person on the screen, not on <body>.
    afterNextRender(() => this.heading()?.nativeElement.focus(), { injector: this.injector });
  }

  private announce(message: string): void {
    this.liveMessage.update((current) => (current === message ? `${message} ` : message));
  }
}

function problemOf(error: unknown): FormsProblem | null {
  return error instanceof HttpErrorResponse ? decodeFormsProblem(error.error, error.status) : null;
}

function messageOf(problem: FormsProblem | null, fallback: string): string {
  if (problem === null || problem.status === 0) return 'Can’t reach 6311arts. Try again.';
  const message = problem.detail ?? fallback;
  return problem.status >= 500 && problem.correlationId !== null
    ? `${message} Quote reference ${problem.correlationId} if you report it.`
    : message;
}
