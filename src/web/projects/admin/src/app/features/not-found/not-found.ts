import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { UiButton, UiEmptyState } from '@6311/ui';

@Component({
  selector: 'cms-not-found',
  imports: [RouterLink, UiButton, UiEmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="cms-title">Not found</h1>
    <ui-empty-state icon="frame" heading="There’s no CMS screen at this address">
      <div uiEmptyActions>
        <a uiButton variant="border-dark" routerLink="/">Back to the dashboard</a>
      </div>
    </ui-empty-state>
  `,
})
export class NotFound {}
