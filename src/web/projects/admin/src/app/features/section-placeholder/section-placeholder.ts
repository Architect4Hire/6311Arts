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
  templateUrl: './section-placeholder.html',
})
export class SectionPlaceholder {
  readonly section = input.required<CmsSection>();
}
