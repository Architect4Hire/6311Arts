import { ChangeDetectionStrategy, Component, inject, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiControl, UiField } from '@6311/ui';
import {
  PAGE_DELETED_STATE,
  PAGE_LIMITS,
  Page,
  PageCreate,
  PageUpdate,
} from '../../../models/page';
import { PageService } from '../../../services/page.service';
import { ContentEditor } from '../../../shared/content-editor';
import { EditorFrame } from '../../../shared/editor-frame/editor-frame';
import { PublishingPanel } from '../../../shared/publishing-panel/publishing-panel';
import {
  CompareRow,
  FieldErrors,
  Focusable,
  htmlExcerpt,
  orDash,
} from '../../../shared/record-editor';
import { RichTextEditor } from '../../../shared/rich-text-editor/rich-text-editor';
import { SaveActions } from '../../../shared/save-actions/save-actions';

interface Draft {
  readonly title: string;
  readonly slug: string;
  /** HTML, as the rich-text editor holds it. */
  readonly body: string;
}

const FIELDS = ['title', 'slug', 'body'] as const;
type Field = (typeof FIELDS)[number];

const EMPTY: Draft = { title: '', slug: '', body: '' };

/**
 * One page (`/admin/pages/new`, `/admin/pages/:id`): title, web address and a rich-text body; then publish,
 * unpublish or (Admins) archive.
 *
 * The body is edited with `<cms-rich-text>`, whose schema is the server's allow-list (ADR 0034); the API sanitizes it
 * regardless and answers with what it stored, which the form then shows.
 */
@Component({
  selector: 'cms-page-editor',
  imports: [
    FormsModule,
    UiControl,
    UiField,
    RichTextEditor,
    EditorFrame,
    PublishingPanel,
    SaveActions,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './page-editor.html',
  styleUrl: './page-editor.scss',
})
export class PageEditor extends ContentEditor<Page, Draft, PageCreate, PageUpdate, Field> {
  private readonly body = viewChild(RichTextEditor);

  protected readonly limits = PAGE_LIMITS;
  protected readonly page = this.record;

  constructor() {
    super({
      api: inject(PageService),
      noun: 'page',
      plural: 'pages',
      route: '/pages',
      label: (page) => page.title,
      address: { path: '6311arts.com/', from: 'title', clash: 'another page or the site already' },
      createdMessage: 'Page created. Publish it when it’s ready.',
      createdState: 'pageCreated',
      empty: () => EMPTY,
      draftOf: (page) => ({ title: page.title, slug: page.slug, body: page.body ?? '' }),
      createOf: (draft) => ({
        title: draft.title.trim(),
        ...(draft.body ? { body: draft.body } : {}),
      }),
      changesOf,
      started: (draft) => draft.title.trim() !== '' || draft.body !== '',
      localErrors: (draft) =>
        draft.title.trim() === '' ? { title: 'Enter the page’s title.' } : {},
      fields: FIELDS,
      deletedState: (page) => ({ [PAGE_DELETED_STATE]: page.title }),
      lifecycle: {
        noun: 'page',
        keepLabel: 'Keep it',
        inSquare: false,
        previewType: 'page',
        unpublishMessage:
          'It goes back to being a draft and leaves the public site. Its web address stays the same, so publishing again restores it.',
        archiveMessage:
          'An archived page is retired from the site, and archiving can’t be undone from Studio. The archive is recorded under your name.',
        deleteText:
          'The page leaves the site and the list, and its web address is free again. An Admin can restore it from Deleted, as a draft.',
        draft: 'A draft: only staff can see it. Publishing needs some text.',
      },
    });
  }

  protected override compare(theirs: Page, draft: Draft): readonly CompareRow[] {
    return [
      { label: 'Title', mine: orDash(draft.title), theirs: theirs.title },
      { label: 'Web address', mine: `/${draft.slug}`, theirs: `/${theirs.slug}` },
      { label: 'Text', mine: htmlExcerpt(draft.body), theirs: htmlExcerpt(theirs.body) },
    ];
  }

  /** The body's error is the rich text's own. */
  protected override focusFallback(errors: FieldErrors<Field>): Focusable | null | undefined {
    return errors.body ? this.body() : null;
  }
}

/** Only what changed; an emptied body is sent as "" so the API clears it. */
function changesOf(page: Page, draft: Draft): PageUpdate {
  const title = draft.title.trim();
  return {
    ...(title !== page.title ? { title } : {}),
    ...(draft.body !== (page.body ?? '') ? { body: draft.body } : {}),
  };
}
