import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { UiIcon } from '@6311/ui';
import { SITE } from '../../../core/site';
import { StaffAccess } from '../staff-access/staff-access';

@Component({
  selector: 'app-footer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, UiIcon, StaffAccess],
  host: { class: 'footer' },
  templateUrl: './footer.html',
})
export class Footer {
  protected site = SITE;
  protected year = new Date().getFullYear();
}
