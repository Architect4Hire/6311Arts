import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { UiButton, UiEmptyState } from '@6311/ui';
import { SessionService } from '../../core/session/session.service';
import { CMS_SECTIONS } from '../layout/sections';

/** Where staff land after signing in. Its panels arrive with the screens they summarise. */
@Component({
  selector: 'cms-dashboard',
  imports: [RouterLink, UiButton, UiEmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard.html',
})
export class Dashboard {
  private readonly session = inject(SessionService);

  protected readonly name = computed(() => this.session.session()?.displayName ?? '');
  protected readonly shortcuts = computed(() =>
    CMS_SECTIONS.filter(
      (section) => section.path !== '' && this.session.hasAnyRole(section.roles),
    ).slice(0, 4),
  );
}
