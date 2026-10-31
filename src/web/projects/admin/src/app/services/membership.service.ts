import { Injectable } from '@angular/core';
import {
  decodeMembershipPlan,
  decodeMembershipPlanList,
  MembershipPlan,
  MembershipPlanCreate,
  MembershipPlanListQuery,
  MembershipPlanSummary,
  MembershipPlanUpdate,
} from '../models/membership';
import { ContentApi } from './content-api';

/**
 * Memberships' HTTP (frontend.md: components never inject `HttpClient`). Every refusal is shown in place by the
 * screen that asked — a field error beside its field, a 409 as a comparison — so none is toasted.
 */
@Injectable({ providedIn: 'root' })
export class MembershipService extends ContentApi<
  MembershipPlan,
  MembershipPlanSummary,
  MembershipPlanCreate,
  MembershipPlanUpdate,
  MembershipPlanListQuery,
  'name'
> {
  constructor() {
    super({
      base: '/api/v1/cms/memberships',
      noun: 'membership plan',
      plural: 'membership plans',
      field: 'name',
      decode: decodeMembershipPlan,
      decodePage: decodeMembershipPlanList,
    });
  }
}
