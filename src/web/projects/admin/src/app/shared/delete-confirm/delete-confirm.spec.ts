import { ConfirmRequest, ConfirmService } from '../../core/confirm/confirm.service';
import { DeleteConfirmRequest, confirmDelete } from './delete-confirm';

/** Answers each question in turn and keeps what was asked. */
class Asked {
  readonly requests: ConfirmRequest[] = [];

  constructor(private readonly answers: boolean[]) {}

  readonly service = {
    confirm: (request: ConfirmRequest) => {
      this.requests.push(request);
      return Promise.resolve(this.answers[this.requests.length - 1] ?? false);
    },
  } as ConfirmService;
}

const PIECE: DeleteConfirmRequest = {
  noun: 'piece',
  label: 'Copper Cuff',
  slug: 'copper-cuff',
  live: true,
  dirty: false,
  inSquare: true,
  keepLabel: 'Keep it',
};

const PAGE: DeleteConfirmRequest = {
  ...PIECE,
  noun: 'page',
  label: 'Privacy Policy',
  slug: 'privacy',
  inSquare: false,
};

describe('confirmDelete', () => {
  it('asks a record Square holds twice, and names Square the second time', async () => {
    const asked = new Asked([true, true]);

    expect(await confirmDelete(asked.service, PIECE)).toBe(true);
    expect(asked.requests).toHaveLength(2);

    // The first is about the site and the CMS.
    expect(asked.requests[0].heading).toBe('Delete this piece?');
    expect(asked.requests[0].message).toContain('6311arts.com/copper-cuff stops working');
    expect(asked.requests[0].message).toContain('restore it from Deleted');

    // The second is about the half a restore does not undo.
    expect(asked.requests[1].heading).toBe('This also deletes it from Square');
    expect(asked.requests[1].message).toContain('“Copper Cuff”');
    expect(asked.requests[1].message).toContain('deletes it from the Square catalogue as well');
    expect(asked.requests[1].message).toContain(
      'Orders, payments and anyone’s membership are untouched.',
    );
    expect(asked.requests[1].confirmLabel).toBe('Delete here and in Square');

    // Both are destructive, and both say so with more than their wording (design-system.md rule 3).
    expect(asked.requests.map((request) => request.tone)).toEqual(['danger', 'danger']);
  });

  it('stops at the Square question when it is declined', async () => {
    const asked = new Asked([true, false]);

    expect(await confirmDelete(asked.service, PIECE)).toBe(false);
    expect(asked.requests).toHaveLength(2);
  });

  it('never reaches the Square question when the first is declined', async () => {
    const asked = new Asked([false]);

    expect(await confirmDelete(asked.service, PIECE)).toBe(false);
    expect(asked.requests).toHaveLength(1);
  });

  it('asks once for a record only the CMS holds', async () => {
    // A second "are you sure" that says nothing new is the kind of prompt people learn to click through.
    const asked = new Asked([true]);

    expect(await confirmDelete(asked.service, PAGE)).toBe(true);
    expect(asked.requests).toHaveLength(1);
    expect(asked.requests[0].message).not.toContain('Square');
  });

  it('says the unsaved edits go too, only when there are some', async () => {
    const clean = new Asked([true, true]);
    await confirmDelete(clean.service, PIECE);
    expect(clean.requests[0].message).not.toContain('unsaved');

    const dirty = new Asked([true, true]);
    await confirmDelete(dirty.service, { ...PIECE, dirty: true });
    expect(dirty.requests[0].message).toContain('Your unsaved changes go with it.');
  });

  it('does not promise the site will change for a record that is not live', async () => {
    const asked = new Asked([true, true]);

    await confirmDelete(asked.service, { ...PIECE, live: false });

    expect(asked.requests[0].message).toContain('The piece leaves the Studio list.');
    expect(asked.requests[0].message).not.toContain('stops working');
  });
});
