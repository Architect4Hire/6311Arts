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
import { HasUnsavedChanges, warnBeforeUnload } from '../../../core/guards/unsaved-changes.guard';
import { ContactMessage, FORMS_CODES, HANDLED_CHIPS } from '../../../models/forms';
import { FormsService } from '../../../services/forms.service';
import { formatInstant } from '../forms-tabs/forms-tabs';
import { messageOf, problemOf } from '../../../core/http/problem-message';

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
  templateUrl: './message-detail.html',
  styleUrl: './message-detail.scss',
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
    this.show(
      'error',
      messageOf(problem, 'The message couldn’t be marked. Try again.', { edits: false }),
    );
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
