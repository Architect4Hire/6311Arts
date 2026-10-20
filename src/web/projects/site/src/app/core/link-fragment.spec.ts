import { TestBed } from '@angular/core/testing';
import { LinkFragment, captureLinkFragment, parseLinkFragment } from './link-fragment';

/** A made-up token in Identity's shape: Base64 with the characters a URL would mangle. */
const TOKEN = 'CfDJ8+a/b=c';
const ENCODED = encodeURIComponent(TOKEN);

function addressBar(pathname: string, hash: string, search = '') {
  const replaceState = vi.fn<History['replaceState']>();
  return {
    location: { pathname, search, hash },
    history: { state: { navigationId: 1 }, replaceState },
    replaceState,
  };
}

function take() {
  return TestBed.inject(LinkFragment).take();
}

describe('captureLinkFragment', () => {
  afterEach(() => {
    // Nothing captured by one test may reach the next.
    take();
  });

  it('takes the user id and token out of a reset link before the app starts', () => {
    const bar = addressBar('/account/reset-password', `#userId=user-1&token=${ENCODED}`);

    captureLinkFragment(bar.location, bar.history);

    expect(bar.replaceState).toHaveBeenCalledWith(
      { navigationId: 1 },
      '',
      '/account/reset-password',
    );
    expect(take()).toEqual({ userId: 'user-1', token: TOKEN });
  });

  it('keeps the query string and drops only the fragment', () => {
    const bar = addressBar(
      '/account/confirm-email',
      `#userId=u&token=${ENCODED}`,
      '?utm_source=email',
    );

    captureLinkFragment(bar.location, bar.history);

    expect(bar.replaceState).toHaveBeenCalledWith(
      expect.anything(),
      '',
      '/account/confirm-email?utm_source=email',
    );
  });

  it('hands the credentials over once', () => {
    const bar = addressBar('/account/confirm-email', `#userId=u&token=${ENCODED}`);
    captureLinkFragment(bar.location, bar.history);

    expect(take()).not.toBeNull();
    expect(take()).toBeNull();
  });

  it('clears a malformed fragment too, and captures nothing from it', () => {
    const bar = addressBar('/account/reset-password', '#userId=u&token=%E0%A4%A');

    captureLinkFragment(bar.location, bar.history);

    expect(bar.replaceState).toHaveBeenCalledOnce();
    expect(take()).toBeNull();
  });

  it('leaves every other page alone', () => {
    const bar = addressBar('/classes', `#userId=u&token=${ENCODED}`);

    captureLinkFragment(bar.location, bar.history);

    expect(bar.replaceState).not.toHaveBeenCalled();
    expect(take()).toBeNull();
  });

  it('does nothing when the link page is opened without a fragment', () => {
    const bar = addressBar('/account/reset-password', '');

    captureLinkFragment(bar.location, bar.history);

    expect(bar.replaceState).not.toHaveBeenCalled();
    expect(take()).toBeNull();
  });
});

describe('parseLinkFragment', () => {
  it('keeps a raw + as a +, which URLSearchParams would turn into a space', () => {
    expect(parseLinkFragment('#userId=u&token=a+b')).toEqual({ userId: 'u', token: 'a+b' });
  });

  it.each(['#userId=u', '#token=t', '#userId=&token=t', '#', '#garbage'])('refuses %s', (hash) => {
    expect(parseLinkFragment(hash)).toBeNull();
  });

  it('refuses a token longer than the gateway accepts', () => {
    expect(parseLinkFragment(`#userId=u&token=${'a'.repeat(2049)}`)).toBeNull();
  });
});
