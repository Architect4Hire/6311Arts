import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { UiButton } from '@6311/ui';

/**
 * A list screen's heading: its title, the lead (projected), and "New …". Other actions go before it with the
 * `listAction` attribute.
 *
 * ```html
 * <cms-list-head heading="Artists" newLabel="New artist" newRoute="/artists/new">The artist roster…</cms-list-head>
 * ```
 */
@Component({
  selector: 'cms-list-head',
  imports: [RouterLink, UiButton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './list-head.html',
  styleUrl: './list-head.scss',
})
export class ListHead {
  readonly heading = input.required<string>();
  readonly newLabel = input.required<string>();
  readonly newRoute = input.required<string>();
}
