import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { SUPPRESS_PROBLEM_TOAST } from '../core/http/http-context';
import { decodeMembershipPlan, decodeMembershipPlanList } from '../models/membership';
import { MEMBERSHIP_PLAN, toMembershipPlanSummary } from '../models/membership.fixtures';
import { MembershipService } from './membership.service';

const BASE = '/api/v1/cms/memberships';

function setup() {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  return {
    memberships: TestBed.inject(MembershipService),
    http: TestBed.inject(HttpTestingController),
  };
}

describe('MembershipService', () => {
  it('lists with the filters as query parameters, and shows failures in place', async () => {
    const { memberships, http } = setup();

    const list = firstValueFrom(
      memberships.list({ cursor: 'abc', limit: 50, status: 'Draft', search: 'gray' }),
    );
    const request = http.expectOne((r) => r.url === BASE);
    expect(request.request.params.get('cursor')).toBe('abc');
    expect(request.request.params.get('status')).toBe('Draft');
    expect(request.request.params.get('search')).toBe('gray');
    expect(request.request.context.get(SUPPRESS_PROBLEM_TOAST)).toBe(true);
    request.flush({ items: [toMembershipPlanSummary(MEMBERSHIP_PLAN)], nextCursor: null });

    expect(await list).toEqual({
      items: [toMembershipPlanSummary(MEMBERSHIP_PLAN)],
      nextCursor: null,
    });
  });

  it('sends an edit and a status command under the ETag it was given', async () => {
    const { memberships, http } = setup();

    const edit = firstValueFrom(
      memberships.update(MEMBERSHIP_PLAN.id, { perks: ['10% off'] }, MEMBERSHIP_PLAN.etag),
    );
    const patch = http.expectOne(`${BASE}/${MEMBERSHIP_PLAN.id}`);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.headers.get('If-Match')).toBe(MEMBERSHIP_PLAN.etag);
    expect(patch.request.body).toEqual({ perks: ['10% off'] });
    patch.flush(MEMBERSHIP_PLAN);
    await edit;

    const published = firstValueFrom(
      memberships.command(MEMBERSHIP_PLAN.id, 'publish', MEMBERSHIP_PLAN.etag),
    );
    const command = http.expectOne(`${BASE}/${MEMBERSHIP_PLAN.id}/publish`);
    expect(command.request.headers.get('If-Match')).toBe(MEMBERSHIP_PLAN.etag);
    command.flush({ ...MEMBERSHIP_PLAN, status: 'Published' });
    expect((await published).status).toBe('Published');
  });

  it('refuses a body that is not a membership plan', async () => {
    const { memberships, http } = setup();

    const read = firstValueFrom(memberships.get(MEMBERSHIP_PLAN.id));
    http.expectOne(`${BASE}/${MEMBERSHIP_PLAN.id}`).flush({ id: MEMBERSHIP_PLAN.id });

    await expect(read).rejects.toThrow('not a membership plan');
  });
});

describe('membership models', () => {
  it('decodes exactly the API shape', () => {
    expect(decodeMembershipPlan(MEMBERSHIP_PLAN)).toEqual(MEMBERSHIP_PLAN);
    expect(decodeMembershipPlan({ ...MEMBERSHIP_PLAN, startingPriceCents: null })?.startingPriceCents).toBeNull();
    expect(decodeMembershipPlan({ ...MEMBERSHIP_PLAN, priceCents: '12000' })?.priceCents).toBe(12_000);

    // What Square owns is read, never guessed at: a cadence it has not reported is null, not a CMS value.
    expect(decodeMembershipPlan({ ...MEMBERSHIP_PLAN, cadence: 'ANNUAL' })?.cadence).toBe('ANNUAL');
    expect(decodeMembershipPlan({ ...MEMBERSHIP_PLAN, startingCadence: 'EVERY_TWO_YEARS' })).toBeNull();
    expect(decodeMembershipPlan({ ...MEMBERSHIP_PLAN, perks: [1] })).toBeNull();
    expect(decodeMembershipPlan({ ...MEMBERSHIP_PLAN, perks: undefined })).toBeNull();
    expect(decodeMembershipPlan({ ...MEMBERSHIP_PLAN, sitePath: undefined })).toBeNull();
    expect(
      decodeMembershipPlanList({
        items: [toMembershipPlanSummary(MEMBERSHIP_PLAN)],
        nextCursor: 'x',
      })?.nextCursor,
    ).toBe('x');
    expect(decodeMembershipPlanList({ items: [{}], nextCursor: null })).toBeNull();
  });
});
