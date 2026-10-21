import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { UiButton, UiEmptyState } from '@6311/ui';

@Component({
  selector: 'cms-not-found',
  imports: [RouterLink, UiButton, UiEmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './not-found.html',
})
export class NotFound {}
