import { Injectable } from '@angular/core';
import {
  decodePage,
  decodePageList,
  Page,
  PageCreate,
  PageListQuery,
  PageSummary,
  PageUpdate,
} from '../models/page';
import { ContentApi } from './content-api';

/**
 * Pages' HTTP (frontend.md: components never inject `HttpClient`). Every refusal is shown in place by the screen
 * that asked — a field error beside its field, a 409 as a comparison — so none is toasted.
 */
@Injectable({ providedIn: 'root' })
export class PageService extends ContentApi<
  Page,
  PageSummary,
  PageCreate,
  PageUpdate,
  PageListQuery,
  'title'
> {
  constructor() {
    super({
      base: '/api/v1/cms/pages',
      noun: 'page',
      plural: 'pages',
      field: 'title',
      decode: decodePage,
      decodePage: decodePageList,
    });
  }
}
