import { Injectable } from '@angular/core';
import {
  decodeExhibition,
  decodeExhibitionPage,
  Exhibition,
  ExhibitionCreate,
  ExhibitionListQuery,
  ExhibitionSummary,
  ExhibitionUpdate,
} from '../models/exhibition';
import { ContentApi } from './content-api';

/**
 * Exhibitions' HTTP (frontend.md: components never inject `HttpClient`). Every refusal is shown in place by the
 * screen that asked — a field error beside its field, a 409 as a comparison — so none is toasted.
 */
@Injectable({ providedIn: 'root' })
export class ExhibitionService extends ContentApi<
  Exhibition,
  ExhibitionSummary,
  ExhibitionCreate,
  ExhibitionUpdate,
  ExhibitionListQuery,
  'title'
> {
  constructor() {
    super({
      base: '/api/v1/cms/exhibitions',
      noun: 'exhibition',
      plural: 'exhibitions',
      field: 'title',
      decode: decodeExhibition,
      decodePage: decodeExhibitionPage,
    });
  }
}
