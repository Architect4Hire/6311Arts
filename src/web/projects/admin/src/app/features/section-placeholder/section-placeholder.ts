import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { UiEmptyState } from '@6311/ui';
import { CmsSection } from '../layout/sections';

/**
 * What a section shows until its own screens are built. The route passes the section in as data; each later
 * prompt swaps this route's `loadComponent` for the real screen.
 */
@Component({
  selector: 'cms-section-placeholder',
  imports: [UiEmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="cms-title">{{ section().label }}</h1>
    <p class="cms-lead">{{ section().summary }}</p>
    <ui-empty-state [icon]="section().icon" heading="Not ready yet">
      <p>The {{ section().label.toLowerCase() }} screens are still being built.</p>
    </ui-empty-state>
  `,
})
export class SectionPlaceholder {
  readonly section = input.required<CmsSection>();
}
