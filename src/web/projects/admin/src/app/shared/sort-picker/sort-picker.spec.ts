import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { ListSort, SortColumn } from '../list-sort';
import { SortPicker } from './sort-picker';

const COLUMNS: readonly SortColumn[] = [
  { key: 'tier', label: 'Tier', kind: 'text' },
  { key: 'updated', label: 'Updated', kind: 'date' },
];

@Component({
  imports: [SortPicker],
  template: `<cms-sort-picker [sort]="sort" />`,
})
class Host {
  // A list whose own order is no column, as the membership tiers' display order is.
  readonly sort = new ListSort(
    COLUMNS,
    { column: 'order', direction: 'asc' },
    'the order the site shows them in',
  );
}

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

async function render() {
  TestBed.configureTestingModule({ providers: [provideRouter([])] });
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  const select = (fixture.nativeElement as HTMLElement).querySelector(
    'select',
  ) as HTMLSelectElement;
  return { fixture, select, router: TestBed.inject(Router), sort: fixture.componentInstance.sort };
}

describe('SortPicker', () => {
  it('offers the list’s own order first, then every column each way round, its first order first', async () => {
    const { fixture, select } = await render();

    expect(text(fixture.nativeElement.querySelector('label'))).toBe('Sort by');
    expect([...select.options].map((option) => [option.value, text(option)])).toEqual([
      ['order', 'the order the site shows them in'],
      ['tier', 'Tier, A to Z'],
      ['-tier', 'Tier, Z to A'],
      ['-updated', 'Updated, newest first'],
      ['updated', 'Updated, oldest first'],
    ]);
    expect(select.value).toBe('order');
  });

  it('sorts the list by the choice and keeps it in the address bar', async () => {
    const { fixture, select, router, sort } = await render();

    select.value = '-updated';
    select.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(sort.current()).toEqual({ column: 'updated', direction: 'desc' });
    expect(router.url).toBe('/?sort=-updated');
    expect(select.value).toBe('-updated');
  });
});
