import { ChangeDetectionStrategy, Component, inject, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiControl, UiField } from '@6311/ui';
import { ARTIST_LIMITS, Artist, ArtistCreate, ArtistUpdate } from '../../../models/artist';
import { ArtistService } from '../../../services/artist.service';
import { ContentEditor } from '../../../shared/content-editor';
import { EditorFrame } from '../../../shared/editor-frame/editor-frame';
import { ImagePanel } from '../../../shared/image-panel/image-panel';
import { PublishingPanel } from '../../../shared/publishing-panel/publishing-panel';
import {
  CompareRow,
  FieldErrors,
  Focusable,
  excerpt,
  imageRow,
  orDash,
  yesNo,
} from '../../../shared/record-editor';
import { SaveActions } from '../../../shared/save-actions/save-actions';

interface Draft {
  readonly name: string;
  readonly slug: string;
  readonly medium: string;
  readonly bio: string;
  readonly active: boolean;
  readonly portraitMediaId: string | null;
}

const FIELDS = ['name', 'slug', 'medium', 'bio', 'portraitMediaId'] as const;
type Field = (typeof FIELDS)[number];

const EMPTY: Draft = {
  name: '',
  slug: '',
  medium: '',
  bio: '',
  active: true,
  portraitMediaId: null,
};

/**
 * One artist (`/admin/artists/new`, `/admin/artists/:id`): name, web address, medium, bio, whether they show here
 * now, and their portrait from the media library; then publish, unpublish or (Admins) archive.
 */
@Component({
  selector: 'cms-artist-editor',
  imports: [FormsModule, UiControl, UiField, EditorFrame, ImagePanel, PublishingPanel, SaveActions],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './artist-editor.html',
  styleUrl: './artist-editor.scss',
})
export class ArtistEditor extends ContentEditor<Artist, Draft, ArtistCreate, ArtistUpdate, Field> {
  private readonly portrait = viewChild(ImagePanel);

  protected readonly limits = ARTIST_LIMITS;
  protected readonly artist = this.record;

  constructor() {
    super({
      api: inject(ArtistService),
      noun: 'artist',
      plural: 'artists',
      route: '/artists',
      label: (artist) => artist.name,
      address: { path: '6311arts.com/artists/', from: 'name' },
      createdMessage: 'Artist created. Publish it when it’s ready.',
      createdState: 'artistCreated',
      empty: () => EMPTY,
      draftOf,
      createOf,
      changesOf,
      started: (draft) =>
        draft.name.trim() !== '' ||
        draft.medium.trim() !== '' ||
        draft.bio.trim() !== '' ||
        !draft.active ||
        draft.portraitMediaId !== null,
      localErrors,
      fields: FIELDS,
      lifecycle: {
        noun: 'artist',
        keepLabel: 'Keep them',
        inSquare: false,
        previewType: 'artist',
        unpublishMessage:
          'They go back to being a draft and leave the public site. Their web address stays the same, so publishing again restores it.',
        archiveMessage:
          'An archived artist is retired from the site, and archiving can’t be undone from Studio. The archive is recorded under your name.',
        deleteText:
          'The artist leaves the site and the list, and their web address is free again. An Admin can restore them from Deleted, as a draft.',
      },
    });
  }

  protected override compare(theirs: Artist, draft: Draft): readonly CompareRow[] {
    return [
      { label: 'Name', mine: orDash(draft.name), theirs: theirs.name },
      { label: 'Web address', mine: `/artists/${draft.slug}`, theirs: `/artists/${theirs.slug}` },
      { label: 'Medium', mine: orDash(draft.medium), theirs: orDash(theirs.medium) },
      { label: 'Bio', mine: excerpt(draft.bio), theirs: excerpt(theirs.bio) },
      { label: 'Shows here now', mine: yesNo(draft.active), theirs: yesNo(theirs.active) },
      imageRow('Portrait', draft.portraitMediaId, theirs.portraitMediaId),
    ];
  }

  protected override focusFallback(errors: FieldErrors<Field>): Focusable | null | undefined {
    return errors.portraitMediaId ? this.portrait()?.focusTarget() : null;
  }
}

function draftOf(artist: Artist): Draft {
  return {
    name: artist.name,
    slug: artist.slug,
    medium: artist.medium ?? '',
    bio: artist.bio ?? '',
    active: artist.active,
    portraitMediaId: artist.portraitMediaId,
  };
}

/** Bio line endings as the API stores them, so an untouched bio never looks edited. */
const normalizeBio = (bio: string) => bio.replace(/\r\n?/g, '\n').trim();

function createOf(draft: Draft): ArtistCreate {
  const medium = draft.medium.trim();
  const bio = normalizeBio(draft.bio);
  return {
    name: draft.name.trim(),
    ...(medium ? { medium } : {}),
    ...(bio ? { bio } : {}),
    active: draft.active,
    ...(draft.portraitMediaId ? { portraitMediaId: draft.portraitMediaId } : {}),
  };
}

/** Only what changed; an emptied medium or bio is sent as "" so the API clears it. */
function changesOf(artist: Artist, draft: Draft): ArtistUpdate {
  const name = draft.name.trim();
  const medium = draft.medium.trim();
  const bio = normalizeBio(draft.bio);
  return {
    ...(name !== artist.name ? { name } : {}),
    ...(medium !== (artist.medium ?? '') ? { medium } : {}),
    ...(bio !== (artist.bio ?? '') ? { bio } : {}),
    ...(draft.active !== artist.active ? { active: draft.active } : {}),
    ...(draft.portraitMediaId !== artist.portraitMediaId
      ? draft.portraitMediaId
        ? { portraitMediaId: draft.portraitMediaId }
        : { removePortrait: true as const }
      : {}),
  };
}

function localErrors(draft: Draft): FieldErrors<Field> {
  return draft.name.trim() === '' ? { name: 'Enter the artist’s name.' } : {};
}
