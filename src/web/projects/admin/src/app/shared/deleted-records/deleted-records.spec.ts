import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { ConfirmService } from '../../core/confirm/confirm.service';
import { SessionService } from '../../core/session/session.service';
import { DeletedRecord, DeletedRecords, DeletedRecordsPage } from './deleted-records';

const ROW: DeletedRecord = {
  id: '0199f0a0-0000-7000-8000-0000000000a1',
  label: 'Privacy Policy',
  slug: 'privacy',
  status: 'Published',
  deletedAt: '2026-10-21T09:00:00+00:00',
  etag: '"0123456789abcdef0123456789abcdef"',
};

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

function button(root: HTMLElement, label: string): HTMLButtonElement | undefined {
  return [...root.querySelectorAll('button')].find((candidate) => text(candidate) === label);
}

async function settle(fixture: ComponentFixture<unknown>) {
  for (let i = 0; i < 5; i++) await Promise.resolve();
  await fixture.whenStable();
  fixture.detectChanges();
}

interface Source {
  list(query: { cursor?: string | null; search?: string }): Observable<DeletedRecordsPage>;
  restore(id: string, etag: string): Observable<unknown>;
}

async function open(source: Source, roles: string[] = ['Admin']) {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });
  const session = TestBed.inject(SessionService);
  const loaded = session.ensureLoaded();
  TestBed.inject((await import('@angular/common/http/testing')).HttpTestingController)
    .expectOne('/bff/session')
    .flush({ displayName: 'Ada', roles });
  await loaded;

  const fixture = TestBed.createComponent(DeletedRecords);
  fixture.componentRef.setInput('noun', 'page');
  fixture.componentRef.setInput('plural', 'pages');
  fixture.componentRef.setInput('source', source);
  fixture.detectChanges();
  await settle(fixture);
  return {
    fixture,
    root: fixture.nativeElement as HTMLElement,
    confirm: TestBed.inject(ConfirmService),
  };
}

describe('DeletedRecords', () => {
  it('lists what was deleted, with no link to an editor', async () => {
    const { root } = await open({
      list: () => of({ items: [ROW], nextCursor: null }),
      restore: () => of(null),
    });

    expect(text(root)).toContain('Privacy Policy');
    expect(text(root)).toContain('/privacy');

    // What it was when it went, not where it is now.
    expect(text(root)).toContain('Published');

    // ADR 0052: a record nothing else can see has no editor to open.
    expect(root.querySelector('a')).toBeNull();
  });

  it('says so when nothing is deleted', async () => {
    const { root } = await open({
      list: () => of({ items: [], nextCursor: null }),
      restore: () => of(null),
    });

    expect(text(root)).toContain('No page has been deleted.');
    expect(root.querySelector('tbody')).toBeNull();
  });

  it('says what the search found when nothing matches it', async () => {
    const { fixture, root } = await open({
      list: () => of({ items: [], nextCursor: null }),
      restore: () => of(null),
    });
    fixture.componentRef.setInput('search', 'privacy');
    await settle(fixture);

    expect(text(root)).toContain('No deleted page’s name contains “privacy”.');
  });

  it('offers a retry when the list cannot be loaded', async () => {
    let fail = true;
    const { fixture, root } = await open({
      list: () =>
        fail ? throwError(() => new Error('down')) : of({ items: [ROW], nextCursor: null }),
      restore: () => of(null),
    });

    expect(text(root)).toContain('couldn’t be loaded');

    fail = false;
    button(root, 'Try again')!.click();
    await settle(fixture);

    expect(text(root)).toContain('Privacy Policy');
  });

  it('restores a record after asking, and reloads the list', async () => {
    const restore = vi.fn(() => of(null));
    let rows = [ROW];
    const { fixture, root, confirm } = await open({
      list: () => of({ items: rows, nextCursor: null }),
      restore,
    });
    const ask = vi.spyOn(confirm, 'confirm').mockResolvedValue(true);

    rows = [];
    button(root, 'Restore')!.click();
    await settle(fixture);

    expect(ask).toHaveBeenCalledWith(
      expect.objectContaining({ heading: 'Restore “Privacy Policy”?' }),
    );
    expect(restore).toHaveBeenCalledWith(ROW.id, ROW.etag);
    expect(text(root)).toContain('No page has been deleted.');
  });

  it('keeps the row and says why when a restore is refused', async () => {
    const { fixture, root, confirm } = await open({
      list: () => of({ items: [ROW], nextCursor: null }),
      restore: () => throwError(() => new Error('taken')),
    });
    vi.spyOn(confirm, 'confirm').mockResolvedValue(true);

    button(root, 'Restore')!.click();
    await settle(fixture);

    expect(text(root)).toContain('couldn’t be restored');
    expect(text(root)).toContain('/privacy');
    expect(text(root)).toContain('Privacy Policy');
  });

  it('shows an Editor the rows but no way to restore one', async () => {
    // Restoring is the Admin's, as deleting was (auth.md "Sensitive operations").
    const { root } = await open(
      { list: () => of({ items: [ROW], nextCursor: null }), restore: () => of(null) },
      ['Editor'],
    );

    expect(text(root)).toContain('Privacy Policy');
    expect(button(root, 'Restore')).toBeUndefined();
  });
});
