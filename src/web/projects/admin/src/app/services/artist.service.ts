import { Injectable } from '@angular/core';
import {
  Artist,
  ArtistCreate,
  ArtistListQuery,
  ArtistSummary,
  ArtistUpdate,
  decodeArtist,
  decodeArtistPage,
} from '../models/artist';
import { ContentApi } from './content-api';

/**
 * The artist roster's HTTP (frontend.md: components never inject `HttpClient`). Every refusal is shown in place by
 * the screen that asked — a field error beside its field, a 409 as a comparison — so none is toasted.
 */
@Injectable({ providedIn: 'root' })
export class ArtistService extends ContentApi<
  Artist,
  ArtistSummary,
  ArtistCreate,
  ArtistUpdate,
  ArtistListQuery,
  'name'
> {
  constructor() {
    super({
      base: '/api/v1/cms/artists',
      noun: 'artist',
      plural: 'artists',
      field: 'name',
      decode: decodeArtist,
      decodePage: decodeArtistPage,
    });
  }
}
