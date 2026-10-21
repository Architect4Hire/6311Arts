import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { ConfirmService } from '../../core/confirm/confirm.service';
import { SessionService } from '../../core/session/session.service';
import { MembershipPlan } from '../../models/membership';
import { MEMBERSHIP_PLAN, toMembershipPlanSummary } from '../../models/membership.fixtures';
import { MembershipList } from './membership-list/membership-list';
import { MembershipPlanEditor } from './membership-plan-editor/membership-plan-editor';

const BASE = '/api/v1/cms/memberships';
const ONE = `${BASE}/${MEMBERSHIP_PLAN.id}`;

async function configure(roles: string[] = ['Editor']) {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });
  const http = TestBed.inject(HttpTestingController);
  const session = TestBed.inject(SessionService);
  const loaded = session.ensureLoaded();
  http.expectOne('/bff/session').flush({ displayName: 'Ada', roles });
  await loaded;
  return { http, confirm: TestBed.inject(ConfirmService), router: TestBed.inject(Router) };
}

async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) {
  for (let i = 0; i < 5; i++) await Promise.resolve();
  await fixture.whenStable();
  fixture.detectChanges();
}

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

function button(root: HTMLElement, label: string): HTMLButtonElement | undefined {
  return [...root.querySelectorAll('button')].find((candidate) => text(candidate) === label);
}

/** The move and remove buttons on a perk say what they do in their label, not their glyph. */
function labelled(root: HTMLElement, label: string): HTMLButtonElement {
  return root.querySelector(`button[aria-label="${label}"]`) as HTMLButtonElement;
}

function control(root: HTMLElement, name: string): HTMLInputElement {
  return root.querySelector(`[name="${name}"]`) as HTMLInputElement;
}

function type(root: HTMLElement, name: string, value: string) {
  const element = control(root, name);
  element.value = value;
  element.dispatchEvent(new Event('input'));
}

async function openEditor(plan: MembershipPlan | null, roles: string[] = ['Editor']) {
  const context = await configure(roles);
  const fixture = TestBed.createComponent(MembershipPlanEditor);
  if (plan) fixture.componentRef.setInput('id', plan.id);
  fixture.detectChanges();
  if (plan) {
    context.http.expectOne(`${BASE}/${plan.id}`).flush(plan);
    await settle(fixture);
  }
  await settle(fixture);
  return { ...context, fixture, root: fixture.nativeElement as HTMLElement };
}

describe('MembershipList', () => {
  it('lists the tiers in the order the page shows them, with the price and its source', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(MembershipList);
    fixture.detectChanges();

    expect(text(fixture.nativeElement.querySelector('[role="status"]'))).toBe(
      'Loading membership tiers…',
    );
    http
      .expectOne((r) => r.url === BASE)
      .flush({
        items: [
          toMembershipPlanSummary(MEMBERSHIP_PLAN),
          {
            ...toMembershipPlanSummary(MEMBERSHIP_PLAN),
            id: 'b',
            name: 'Black',
            slug: 'black-membership',
            featured: true,
            displayOrder: 2,
            discountPercent: 10,
            priceCents: 30_000,
            cadence: 'ANNUAL',
          },
        ],
        nextCursor: null,
      });
    await settle(fixture);

    const rows = [...(fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr')];
    expect(rows.map((row) => text(row.querySelector('th a')))).toEqual(['Gray', 'Black']);

    // Until Square reports a price the CMS's draft figure is shown, and it says so.
    expect(text(rows[0])).toContain('$100.00');
    expect(text(rows[0])).toContain('Not in Square yet');
    expect(text(rows[1])).toContain('$300.00');
    expect(text(rows[1])).toContain('In Square · Annual');
    expect(text(rows[1])).toContain('Featured');
    expect(rows[0].querySelector('a')?.getAttribute('href')).toBe(
      `/memberships/${MEMBERSHIP_PLAN.id}`,
    );
  });

  it('offers a retry when the tiers cannot be loaded, and an empty state when there are none', async () => {
    // frontend.md: every data surface has loading, empty, error and success states. All four existed in the
    // component and only the happy path was tested, so a 503 leaving "Loading membership tiers…" on screen for good
    // would have passed (test-gap review).
    const { http } = await configure();
    const fixture = TestBed.createComponent(MembershipList);
    fixture.detectChanges();

    expect(text(fixture.nativeElement)).toContain('Loading membership tiers…');

    http.expectOne((r) => r.url === BASE).flush(null, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);

    expect(text(fixture.nativeElement)).toContain('The membership tiers couldn’t be loaded');

    button(fixture.nativeElement, 'Try again')!.click();
    http.expectOne((r) => r.url === BASE).flush({ items: [], nextCursor: null });
    await settle(fixture);

    expect(fixture.nativeElement.querySelector('ui-empty-state')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('tbody')).toBeNull();
  });
});

describe('MembershipPlanEditor', () => {
  it('offers a retry when the record cannot be loaded, and asks again when it is used', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(MembershipPlanEditor);
    fixture.componentRef.setInput('id', MEMBERSHIP_PLAN.id);
    fixture.detectChanges();

    http.expectOne(ONE).flush(null, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);

    expect(text(fixture.nativeElement)).toContain('This tier couldn’t be loaded');

    // The retry asks again rather than showing a form built from nothing.
    button(fixture.nativeElement, 'Try again')!.click();
    http
      .expectOne(ONE)
      .flush(
        { code: 'resource.not_found', title: 'Not found.' },
        { status: 404, statusText: 'Not Found' },
      );
    await settle(fixture);

    expect(text(fixture.nativeElement)).not.toContain('This tier couldn’t be loaded');
  });

  it('says there is no such record on a 404, and offers the way back', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(MembershipPlanEditor);
    fixture.componentRef.setInput('id', MEMBERSHIP_PLAN.id);
    fixture.detectChanges();

    http
      .expectOne(ONE)
      .flush(
        { code: 'resource.not_found', title: 'Not found.' },
        { status: 404, statusText: 'Not Found' },
      );
    await settle(fixture);

    const root = fixture.nativeElement as HTMLElement;
    expect(text(root)).toContain('There’s no such tier');
    expect(root.querySelector('a[href="/memberships"]')).not.toBeNull();
    expect(button(root, 'Try again')).toBeUndefined();
  });

  it('creates a tier with its key made from the name, its perks in order and its price in cents', async () => {
    const { http, fixture, root, router } = await openEditor(null);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    type(root, 'name', 'Gray');
    await settle(fixture);
    expect(control(root, 'slug').value).toBe('gray');

    type(root, 'perk-0', '5% off every class');
    button(root, 'Add a perk')!.click();
    await settle(fixture);
    type(root, 'perk-1', 'Annual membership party');
    type(root, 'discountPercent', '5');
    type(root, 'price', '100');
    await settle(fixture);
    button(root, 'Create tier')!.click();

    const create = http.expectOne(BASE);
    expect(create.request.body).toEqual({
      name: 'Gray',
      slug: 'gray',
      perks: ['5% off every class', 'Annual membership party'],
      discountPercent: 5,
      startingPriceCents: 10_000,
      startingCadence: 'Annual',
    });
    create.flush(MEMBERSHIP_PLAN, { status: 201, statusText: 'Created' });
    await settle(fixture);

    expect(navigate).toHaveBeenCalledWith(['/memberships', MEMBERSHIP_PLAN.id], {
      replaceUrl: true,
      state: { membershipPlanCreated: true },
    });
  });

  it('is not dirty just for loading a tier, and sends only what changed', async () => {
    const { http, fixture, root } = await openEditor(MEMBERSHIP_PLAN);

    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);

    labelled(root, 'Move perk 2 up').click();
    await settle(fixture);
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);

    button(root, 'Save')!.click();
    const patch = http.expectOne(ONE);
    expect(patch.request.headers.get('If-Match')).toBe(MEMBERSHIP_PLAN.etag);
    expect(patch.request.body).toEqual({
      perks: ['Annual membership party', '5% off every class & workshop', '6311arts tote'],
    });
  });

  it('shows a refused publish on the field it belongs to', async () => {
    const { http, fixture, root } = await openEditor({
      ...MEMBERSHIP_PLAN,
      perks: [],
      startingPriceCents: null,
    });

    button(root, 'Publish')!.click();
    http.expectOne(`${ONE}/publish`).flush(
      {
        code: 'validation.failed',
        detail: 'This tier isn’t ready to publish yet.',
        errors: {
          startingPriceCents: ['Set what the tier costs: the page shows a price on every card.'],
          perks: [
            'Add at least one perk: a tier with nothing listed gives members nothing to read.',
          ],
        },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle(fixture);

    expect(text(root)).toContain('This tier isn’t ready to publish yet.');
    expect(text(root)).toContain('Add at least one perk');
    expect(control(root, 'price').getAttribute('aria-invalid')).toBe('true');
  });

  it('on a 409 shows both versions and can keep the editor’s changes', async () => {
    const { http, fixture, root } = await openEditor(MEMBERSHIP_PLAN);
    const theirs: MembershipPlan = {
      ...MEMBERSHIP_PLAN,
      discountPercent: 8,
      etag: '"theirs"',
    };

    type(root, 'discountPercent', '7');
    await settle(fixture);
    button(root, 'Save')!.click();
    http
      .expectOne(ONE)
      .flush({ code: 'concurrency.conflict' }, { status: 409, statusText: 'Conflict' });
    await settle(fixture);
    http.expectOne(ONE).flush(theirs);
    await settle(fixture);

    const comparison = text(root.querySelector('.compare'));
    expect(comparison).toContain('7%');
    expect(comparison).toContain('8%');

    button(root, 'Keep my changes')!.click();
    const retry = http.expectOne(ONE);
    expect(retry.request.headers.get('If-Match')).toBe('"theirs"');
    expect(retry.request.body).toEqual({ discountPercent: 7 });
  });

  it('shows Square’s price read-only once Square holds the plan, and never sends it', async () => {
    // square.md, ADR 0005: after the first sync the price and the cadence are Square's record.
    const synced: MembershipPlan = {
      ...MEMBERSHIP_PLAN,
      status: 'Published',
      publishedAt: MEMBERSHIP_PLAN.createdAt,
      startingPriceEditable: false,
      squareHasRecord: true,
      priceCents: 12_000,
      cadence: 'ANNUAL',
    };
    const { http, fixture, root } = await openEditor(synced);

    expect(control(root, 'price')).toBeNull();
    expect(text(root)).toContain('Price in Square: $120.00');
    expect(text(root)).toContain('Bills in Square: Annual');
    expect(text(root)).toContain('Square holds this tier’s price and billing now.');

    type(root, 'name', 'Gray Plus');
    await settle(fixture);
    button(root, 'Save')!.click();
    expect(http.expectOne(ONE).request.body).toEqual({ name: 'Gray Plus' });
  });

  it('offers archive to Admins only, and asks first', async () => {
    const published: MembershipPlan = {
      ...MEMBERSHIP_PLAN,
      status: 'Published',
      publishedAt: MEMBERSHIP_PLAN.createdAt,
    };
    const editor = await openEditor(published);
    expect(button(editor.root, 'Archive')).toBeUndefined();
    TestBed.resetTestingModule();

    const { http, fixture, root, confirm } = await openEditor(published, ['Admin']);
    const ask = vi.spyOn(confirm, 'confirm').mockResolvedValue(true);
    button(root, 'Archive')!.click();
    await settle(fixture);

    expect(ask).toHaveBeenCalledWith(expect.objectContaining({ heading: 'Archive this tier?' }));
    http.expectOne(`${ONE}/archive`).flush({ ...published, status: 'Archived', etag: '"v2"' });
    await settle(fixture);
    expect(text(root.querySelector('header'))).toContain('Archived');
  });

  it('schedules the tier and offers a preview link for the memberships page', async () => {
    const { http, fixture, root } = await openEditor(MEMBERSHIP_PLAN);

    type(root, 'scheduleDate', '2026-12-24');
    type(root, 'scheduleTime', '17:00');
    await settle(fixture);
    button(root, 'Schedule')!.click();

    const schedule = http.expectOne(`${ONE}/schedule`);
    expect(schedule.request.headers.get('If-Match')).toBe(MEMBERSHIP_PLAN.etag);
    expect(schedule.request.body).toEqual({ scheduledFor: '2026-12-24T23:00:00+00:00' });
    schedule.flush({
      ...MEMBERSHIP_PLAN,
      status: 'Scheduled',
      scheduledFor: '2026-12-24T23:00:00+00:00',
      etag: '"v2"',
    });
    await settle(fixture);
    expect(text(root)).toContain('Publishing on');

    button(root, 'Preview')!.click();
    const minted = http.expectOne('/api/v1/cms/preview');
    expect(minted.request.body).toEqual({
      entityType: 'membership-plan',
      entityId: MEMBERSHIP_PLAN.id,
    });
    minted.flush({
      token: 'CfDJ8-token',
      path: '/preview/CfDJ8-token',
      expiresAt: '2026-10-20T12:30:00+00:00',
    });
    await settle(fixture);
    expect(
      (root.querySelector('a[href^="/preview/"]') as HTMLAnchorElement).getAttribute('href'),
    ).toBe('/preview/CfDJ8-token');
  });
});
