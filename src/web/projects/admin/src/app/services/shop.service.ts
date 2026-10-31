import { HttpHeaders } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map } from 'rxjs';
import {
  ShopImportPreview,
  ShopImportRequest,
  ShopImportResult,
  ShopItem,
  ShopItemCreate,
  ShopItemSummary,
  ShopItemUpdate,
  ShopListQuery,
  decodeShopImportPreview,
  decodeShopImportResult,
  decodeShopItem,
  decodeShopPage,
} from '../models/shop';
import { decoded, quiet } from './api';
import { ContentApi } from './content-api';

/**
 * The shop's HTTP (frontend.md: components never inject `HttpClient`). Every refusal is shown in place by the screen
 * that asked — a field error beside its field, a 409 as a comparison — so none is toasted.
 *
 * There is no price or stock write here: those are Square's record, and the commands that request a change arrive
 * in 5.4a and 5.4b (ADR 0032).
 */
@Injectable({ providedIn: 'root' })
export class ShopService extends ContentApi<
  ShopItem,
  ShopItemSummary,
  ShopItemCreate,
  ShopItemUpdate,
  ShopListQuery,
  'title'
> {
  constructor() {
    super({
      base: '/api/v1/cms/shop',
      noun: 'shop item',
      plural: 'shop items',
      field: 'title',
      decode: decodeShopItem,
      decodePage: decodeShopPage,
    });
  }

  /** What a CSV would create, row by row, with each row's problems. Writes nothing. */
  previewImport(request: ShopImportRequest): Observable<ShopImportPreview> {
    return this.http
      .post<unknown>(`${this.api.base}/import/preview`, request, { context: quiet() })
      .pipe(map((body) => decoded(decodeShopImportPreview(body), 'import preview')));
  }

  /**
   * Creates every row of the CSV as a Draft, or none of them. The key makes a retry of the same file safe: the
   * API answers it with the first result rather than importing twice (api-contract.md "Idempotency").
   */
  importItems(request: ShopImportRequest, idempotencyKey: string): Observable<ShopImportResult> {
    return this.http
      .post<unknown>(`${this.api.base}/import`, request, {
        headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }),
        context: quiet(),
      })
      .pipe(map((body) => decoded(decodeShopImportResult(body), 'import result')));
  }
}
