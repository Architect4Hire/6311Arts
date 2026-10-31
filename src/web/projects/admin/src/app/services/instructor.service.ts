import { Injectable } from '@angular/core';
import {
  decodeInstructor,
  decodeInstructorPage,
  Instructor,
  InstructorCreate,
  InstructorListQuery,
  InstructorSummary,
  InstructorUpdate,
} from '../models/instructor';
import { ContentApi } from './content-api';

/**
 * The instructors' HTTP (frontend.md: components never inject `HttpClient`). Every refusal is shown in place by
 * the screen that asked — a field error beside its field, a 409 as a comparison — so none is toasted.
 */
@Injectable({ providedIn: 'root' })
export class InstructorService extends ContentApi<
  Instructor,
  InstructorSummary,
  InstructorCreate,
  InstructorUpdate,
  InstructorListQuery,
  'name'
> {
  constructor() {
    super({
      base: '/api/v1/cms/instructors',
      noun: 'instructor',
      plural: 'instructors',
      field: 'name',
      decode: decodeInstructor,
      decodePage: decodeInstructorPage,
    });
  }
}
