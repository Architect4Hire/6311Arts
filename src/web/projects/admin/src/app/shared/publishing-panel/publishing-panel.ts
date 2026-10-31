import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  viewChild,
} from '@angular/core';
import { UiButton } from '@6311/ui';
import { SessionService } from '../../core/session/session.service';
import { ROLES } from '../../models/session';
import { LifecycleActions, LifecycleRecord, statusTextOf } from '../content-lifecycle';
import { PreviewLinkButton } from '../preview-link/preview-link';
import { ScheduleControl } from '../schedule-control/schedule-control';

/**
 * A content editor's Publishing panel, the same for every publishable type (content.md "Lifecycle"): where the
 * record stands, publish / unpublish / (Admins) archive, the schedule, a preview link and (Admins) the delete.
 *
 * ```html
 * <cms-publishing-panel [lifecycle]="lifecycle" [record]="artist" [dirty]="dirty()" [busy]="busy()" />
 * ```
 *
 * It only shows and asks: the editor's {@link ContentLifecycle} sends every command and owns what follows.
 */
@Component({
  selector: 'cms-publishing-panel',
  imports: [UiButton, PreviewLinkButton, ScheduleControl],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './publishing-panel.html',
  styleUrl: './publishing-panel.scss',
})
export class PublishingPanel {
  readonly lifecycle = input.required<LifecycleActions>();
  /** The record as saved. */
  readonly record = input.required<LifecycleRecord>();
  /** Unsaved edits: the status commands act on the saved version, so they wait. */
  readonly dirty = input.required<boolean>();
  /** A save, command or delete in flight. */
  readonly busy = input.required<boolean>();

  private readonly session = inject(SessionService);
  private readonly injector = inject(Injector);
  private readonly heading = viewChild.required<ElementRef<HTMLElement>>('heading');

  protected readonly copy = computed(() => this.lifecycle().copy);
  protected readonly statusText = computed(() => statusTextOf(this.record(), this.copy()));
  protected readonly isAdmin = computed(() => this.session.hasAnyRole([ROLES.admin]));
  protected readonly canPublish = computed(() => {
    const status = this.record().status;
    return status === 'Draft' || status === 'Scheduled';
  });
  protected readonly canArchive = computed(
    () => this.isAdmin() && this.record().status !== 'Archived',
  );

  /** Publish, unpublish, archive or unschedule; once applied, focus goes to the heading. */
  protected async run(command: 'publish' | 'unpublish' | 'archive' | 'unschedule'): Promise<void> {
    // The button pressed may be gone (Publish, once published), so focus goes to the panel's heading.
    if (await this.lifecycle()[command]()) {
      afterNextRender(() => this.heading().nativeElement.focus(), { injector: this.injector });
    }
  }

  protected schedule(scheduledFor: string): void {
    void this.lifecycle().schedule(scheduledFor);
  }

  protected remove(): void {
    void this.lifecycle().remove();
  }
}
