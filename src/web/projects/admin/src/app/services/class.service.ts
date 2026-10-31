import { Injectable } from '@angular/core';
import {
  ClassCreate,
  ClassListQuery,
  ClassOffering,
  ClassSummary,
  ClassUpdate,
  decodeClassOffering,
  decodeClassPage,
} from '../models/class';
import { ContentApi } from './content-api';

/**
 * Classes' HTTP (frontend.md: components never inject `HttpClient`). Every refusal is shown in place by the screen
 * that asked — a field error beside its field, a 409 as a comparison — so none is toasted.
 *
 * There is no price or seat write here: those are Square's record, and the commands that request a change arrive in
 * 5.4a (ADR 0032).
 */
@Injectable({ providedIn: 'root' })
export class ClassService extends ContentApi<
  ClassOffering,
  ClassSummary,
  ClassCreate,
  ClassUpdate,
  ClassListQuery,
  'title'
> {
  constructor() {
    super({
      base: '/api/v1/cms/classes',
      noun: 'class',
      plural: 'classes',
      field: 'title',
      decode: decodeClassOffering,
      decodePage: decodeClassPage,
    });
  }
}
