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
  template: `
    <h1 class="cms-title">Welcome, {{ name() }}</h1>
    <p class="cms-lead">This is where you edit what the public site shows.</p>

    <!-- Not "nothing needs attention": no data stands behind this panel yet, so it must not claim an all-clear. -->
    <ui-empty-state icon="grid" heading="Attention list not connected yet">
      <p>
        Drafts waiting to publish, sync problems and new form submissions will be listed here once those screens
        exist.
      </p>
      <div uiEmptyActions>
        @for (section of shortcuts(); track section.path) {
          <a uiButton variant="border-dark" [routerLink]="'/' + section.path">{{
            section.label
          }}</a>
        }
      </div>
    </ui-empty-state>
  `,
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
