import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { ListSort, SortColumn } from '../list-sort';
import { Sortable } from '../sortable';
import { SortHeader } from './sort-header';

const COLUMNS: readonly SortColumn[] = [
  { key: 'name', label: 'Name', kind: 'text' },
  { key: 'updated', label: 'Updated', kind: 'date' },
  {
    key: 'status',
    label: 'Status',
    kind: 'status',
    words: { asc: 'new first', desc: 'declined first' },
  },
];

@Component({
  imports: [Sortable, SortHeader],
  template: `
    <table class="cms-table" [cmsSortable]="sort">
      <thead>
        <tr>
          <th scope="col" cmsSortHeader="name">Name</th>
          <th scope="col" cmsSortHeader="updated">Updated</th>
          <th scope="col" cmsSortHeader="status">Status</th>
          <th scope="col">Notes</th>
        </tr>
      </thead>
    </table>
  `,
})
class Host {
  readonly sort = new ListSort(COLUMNS, { column: 'name', direction: 'asc' });
}

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

async function render(url = '/') {
  TestBed.configureTestingModule({ providers: [provideRouter([])] });
  const router = TestBed.inject(Router);
  await router.navigateByUrl(url);
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  const root = fixture.nativeElement as HTMLElement;
  const header = (label: string) =>
    [...root.querySelectorAll('th')].find((th) => text(th) === label) as HTMLElement;
  const click = async (label: string) => {
    header(label).querySelector('button')!.click();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  return { fixture, root, router, header, click, sort: fixture.componentInstance.sort };
}

describe('SortHeader', () => {
  it('makes each sortable header one button and marks only the sorted one', async () => {
    const { header } = await render();

    expect(header('Name').getAttribute('aria-sort')).toBe('ascending');
    expect(header('Updated').hasAttribute('aria-sort')).toBe(false);
    expect(header('Name').querySelector('button')?.getAttribute('type')).toBe('button');
    expect(header('Notes').querySelector('button')).toBeNull();
  });

  it('sorts a date newest first on the first click and turns it round on the second', async () => {
    const { header, click, router, sort } = await render();

    await click('Updated');
    expect(header('Updated').getAttribute('aria-sort')).toBe('descending');
    expect(header('Name').hasAttribute('aria-sort')).toBe(false);
    expect(sort.param()).toBe('-updated');
    expect(router.url).toBe('/?sort=-updated');

    await click('Updated');
    expect(header('Updated').getAttribute('aria-sort')).toBe('ascending');
    expect(router.url).toBe('/?sort=updated');
  });

  it('keeps the list’s own order out of the address bar', async () => {
    const { click, router } = await render();

    await click('Name');
    expect(router.url).toBe('/?sort=-name');
    await click('Name');
    expect(router.url).toBe('/');
  });

  it('opens in the order the address bar names, and in its own order for a column it does not sort by', async () => {
    const named = await render('/?sort=-status');
    expect(named.header('Status').getAttribute('aria-sort')).toBe('descending');
    TestBed.resetTestingModule();

    const unknown = await render('/?sort=colour');
    expect(unknown.header('Name').getAttribute('aria-sort')).toBe('ascending');
    expect(unknown.sort.param()).toBe('name');
  });

  it('puts the order into words, and announces it only when it changes', async () => {
    const { click, sort } = await render();

    expect(sort.description()).toBe('Name, A to Z');
    expect(sort.announcement()).toBe('');
    expect(sort.announcement()).toBe('');

    await click('Status');
    expect(sort.description()).toBe('Status, new first');
    expect(sort.announcement()).toBe('Sorted by Status, new first. ');
    expect(sort.announcement()).toBe('');
  });
});
