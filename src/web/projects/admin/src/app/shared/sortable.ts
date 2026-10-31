import { Directive, input } from '@angular/core';
import { ListSort } from './list-sort';

/**
 * Makes a CMS table's headers sortable (ADR 0062): `<table class="cms-table" [cmsSortable]="sort">`, then
 * `<th scope="col" cmsSortHeader="name">Name</th>` for each column the list sorts by. A header without it stays a
 * plain label.
 */
@Directive({ selector: 'table[cmsSortable]' })
export class Sortable {
  readonly cmsSortable = input.required<ListSort>();
}
