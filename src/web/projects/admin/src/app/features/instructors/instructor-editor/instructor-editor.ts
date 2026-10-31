import { ChangeDetectionStrategy, Component, inject, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiControl, UiField } from '@6311/ui';
import {
  INSTRUCTOR_LIMITS,
  Instructor,
  InstructorCreate,
  InstructorUpdate,
  isInstagramHandle,
  isWebsite,
  normalizeInstagram,
} from '../../../models/instructor';
import { InstructorService } from '../../../services/instructor.service';
import { ContentEditor } from '../../../shared/content-editor';
import { EditorFrame } from '../../../shared/editor-frame/editor-frame';
import { ImagePanel } from '../../../shared/image-panel/image-panel';
import { ArtistPicker, PickedArtist } from '../../../shared/pickers/artist-picker/artist-picker';
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
  readonly website: string;
  readonly instagram: string;
  /** The artist they also are, so their page can link to their work in the shop (ADR 0043). */
  readonly artist: PickedArtist | null;
}

const FIELDS = [
  'name',
  'slug',
  'medium',
  'bio',
  'portraitMediaId',
  'website',
  'instagram',
  'artistId',
] as const;
type Field = (typeof FIELDS)[number];

const EMPTY: Draft = {
  name: '',
  slug: '',
  medium: '',
  bio: '',
  active: true,
  portraitMediaId: null,
  website: '',
  instagram: '',
  artist: null,
};

/**
 * One instructor (`/admin/instructors/new`, `/admin/instructors/:id`, ADR 0043): name, web address, what they teach,
 * bio, whether they teach here now, their website and Instagram, the artist they also are, and their portrait from the
 * media library; then publish, unpublish, schedule or (Admins) archive.
 */
@Component({
  selector: 'cms-instructor-editor',
  imports: [
    FormsModule,
    UiControl,
    UiField,
    ArtistPicker,
    EditorFrame,
    ImagePanel,
    PublishingPanel,
    SaveActions,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './instructor-editor.html',
  styleUrl: './instructor-editor.scss',
})
export class InstructorEditor extends ContentEditor<
  Instructor,
  Draft,
  InstructorCreate,
  InstructorUpdate,
  Field
> {
  private readonly portrait = viewChild(ImagePanel);

  protected readonly limits = INSTRUCTOR_LIMITS;
  protected readonly instructor = this.record;

  constructor() {
    super({
      api: inject(InstructorService),
      noun: 'instructor',
      plural: 'instructors',
      route: '/instructors',
      label: (instructor) => instructor.name,
      address: { path: '6311arts.com/instructors/', from: 'name' },
      createdMessage: 'Instructor created. Publish it when it’s ready.',
      createdState: 'instructorCreated',
      empty: () => EMPTY,
      draftOf,
      createOf,
      changesOf,
      started: (draft) =>
        draft.name.trim() !== '' ||
        draft.medium.trim() !== '' ||
        draft.bio.trim() !== '' ||
        !draft.active ||
        draft.portraitMediaId !== null ||
        draft.website.trim() !== '' ||
        draft.instagram.trim() !== '' ||
        draft.artist !== null,
      localErrors,
      fields: FIELDS,
      lifecycle: {
        noun: 'instructor',
        keepLabel: 'Keep them',
        inSquare: false,
        previewType: 'instructor',
        unpublishMessage:
          'They go back to being a draft and leave the public site. Their web address stays the same, so publishing again restores it.',
        archiveMessage:
          'An archived instructor is retired from the site, and archiving can’t be undone from Studio. The archive is recorded under your name.',
        deleteText:
          'The instructor leaves the site and the list, and their web address is free again. An Admin can restore them from Deleted, as a draft.',
      },
    });
  }

  protected override compare(theirs: Instructor, draft: Draft): readonly CompareRow[] {
    return [
      { label: 'Name', mine: orDash(draft.name), theirs: theirs.name },
      {
        label: 'Web address',
        mine: `/instructors/${draft.slug}`,
        theirs: `/instructors/${theirs.slug}`,
      },
      { label: 'Medium', mine: orDash(draft.medium), theirs: orDash(theirs.medium) },
      { label: 'Bio', mine: excerpt(draft.bio), theirs: excerpt(theirs.bio) },
      { label: 'Teaches here now', mine: yesNo(draft.active), theirs: yesNo(theirs.active) },
      { label: 'Website', mine: orDash(draft.website), theirs: orDash(theirs.website) },
      { label: 'Instagram', mine: orDash(draft.instagram), theirs: orDash(theirs.instagram) },
      { label: 'Artist', mine: orDash(draft.artist?.name), theirs: orDash(theirs.artist?.name) },
      imageRow('Portrait', draft.portraitMediaId, theirs.portraitMediaId),
    ];
  }

  protected override fieldsOf(keys: readonly string[]): readonly string[] {
    return keys.map((key) => (key === 'artist' ? 'artistId' : key));
  }

  protected override focusFallback(errors: FieldErrors<Field>): Focusable | null | undefined {
    return errors.portraitMediaId ? this.portrait()?.focusTarget() : null;
  }
}

function draftOf(instructor: Instructor): Draft {
  return {
    name: instructor.name,
    slug: instructor.slug,
    medium: instructor.medium ?? '',
    bio: instructor.bio ?? '',
    active: instructor.active,
    portraitMediaId: instructor.portraitMediaId,
    website: instructor.website ?? '',
    instagram: instructor.instagram ?? '',
    artist: instructor.artist
      ? { id: instructor.artist.id, name: instructor.artist.name, status: instructor.artist.status }
      : null,
  };
}

/** Bio line endings as the API stores them, so an untouched bio never looks edited. */
const normalizeBio = (bio: string) => bio.replace(/\r\n?/g, '\n').trim();

function createOf(draft: Draft): InstructorCreate {
  const medium = draft.medium.trim();
  const bio = normalizeBio(draft.bio);
  const website = draft.website.trim();
  const instagram = normalizeInstagram(draft.instagram);
  return {
    name: draft.name.trim(),
    ...(medium ? { medium } : {}),
    ...(bio ? { bio } : {}),
    active: draft.active,
    ...(draft.portraitMediaId ? { portraitMediaId: draft.portraitMediaId } : {}),
    ...(website ? { website } : {}),
    ...(instagram ? { instagram } : {}),
    ...(draft.artist ? { artistId: draft.artist.id } : {}),
  };
}

/** Only what changed; an emptied medium, bio, website or handle is sent as "" so the API clears it. */
function changesOf(instructor: Instructor, draft: Draft): InstructorUpdate {
  const name = draft.name.trim();
  const medium = draft.medium.trim();
  const bio = normalizeBio(draft.bio);
  const website = draft.website.trim();
  const instagram = normalizeInstagram(draft.instagram);
  const artistId = draft.artist?.id ?? null;
  return {
    ...(name !== instructor.name ? { name } : {}),
    ...(medium !== (instructor.medium ?? '') ? { medium } : {}),
    ...(bio !== (instructor.bio ?? '') ? { bio } : {}),
    ...(draft.active !== instructor.active ? { active: draft.active } : {}),
    ...(draft.portraitMediaId !== instructor.portraitMediaId
      ? draft.portraitMediaId
        ? { portraitMediaId: draft.portraitMediaId }
        : { removePortrait: true as const }
      : {}),
    ...(website !== (instructor.website ?? '') ? { website } : {}),
    ...(instagram !== (instructor.instagram ?? '') ? { instagram } : {}),
    ...(artistId !== (instructor.artist?.id ?? null)
      ? artistId
        ? { artistId }
        : { removeArtist: true as const }
      : {}),
  };
}

function localErrors(draft: Draft): FieldErrors<Field> {
  const errors: FieldErrors<Field> = {};
  if (draft.name.trim() === '') errors.name = 'Enter the instructor’s name.';
  const website = draft.website.trim();
  if (website !== '' && !isWebsite(website)) {
    errors.website =
      'Enter the full address, starting https:// (for example https://janedoe.studio).';
  }
  const instagram = normalizeInstagram(draft.instagram);
  if (instagram !== '' && !isInstagramHandle(instagram)) {
    errors.instagram = 'Use letters, digits, periods and underscores, up to 30 characters.';
  }
  return errors;
}
