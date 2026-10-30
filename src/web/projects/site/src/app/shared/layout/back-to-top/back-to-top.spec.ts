import { TestBed } from '@angular/core/testing';
import { BackToTop } from './back-to-top';

function stubMotion(reduced: boolean) {
  // jsdom has no matchMedia.
  vi.stubGlobal('matchMedia', () => ({ matches: reduced }));
}

function scrollTo(y: number) {
  Object.defineProperty(window, 'scrollY', { configurable: true, value: y });
  window.dispatchEvent(new Event('scroll'));
}

async function render() {
  const fixture = TestBed.createComponent(BackToTop);
  await fixture.whenStable();
  const host = fixture.nativeElement as HTMLElement;
  return { fixture, host, button: host.querySelector('button')! };
}

describe('BackToTop', () => {
  let scroll: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    stubMotion(false);
    // jsdom does not implement scrolling.
    scroll = vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined);
    Object.defineProperty(window, 'innerHeight', { configurable: true, value: 800 });
    Object.defineProperty(window, 'scrollY', { configurable: true, value: 0 });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    document.querySelector('.skip')?.remove();
  });

  it('is a named button', async () => {
    const { button } = await render();

    expect(button.getAttribute('type')).toBe('button');
    expect(button.getAttribute('aria-label')).toBe('Back to top');
  });

  it('stays hidden at the top of the page', async () => {
    const { host } = await render();

    expect(host.classList).not.toContain('to-top--shown');
  });

  it('appears once the visitor is a screen down, and goes again near the top', async () => {
    const { fixture, host } = await render();

    scrollTo(1200);
    await fixture.whenStable();
    expect(host.classList).toContain('to-top--shown');

    scrollTo(100);
    await fixture.whenStable();
    expect(host.classList).not.toContain('to-top--shown');
  });

  it('scrolls smoothly to the top', async () => {
    const { button } = await render();

    button.click();

    expect(scroll).toHaveBeenCalledWith({ top: 0, behavior: 'smooth' });
  });

  it('jumps without animating when motion is reduced', async () => {
    stubMotion(true);
    const { button } = await render();

    button.click();

    expect(scroll).toHaveBeenCalledWith({ top: 0, behavior: 'auto' });
  });

  function addSkipLink(): HTMLAnchorElement {
    const skip = document.createElement('a');
    skip.className = 'skip';
    skip.href = '#content';
    document.body.prepend(skip);
    return skip;
  }

  // A keyboard press on a button fires a click with a click count (detail) of 0.
  it('hands keyboard focus to the skip link, the first stop on the page', async () => {
    const skip = addSkipLink();
    const { button } = await render();

    button.click();

    expect(document.activeElement).toBe(skip);
  });

  // Focusing the skip link reveals it; a mouse or touch user has no use for it.
  it('leaves the skip link alone after a pointer click', async () => {
    const skip = addSkipLink();
    const { button } = await render();

    button.dispatchEvent(new MouseEvent('click', { bubbles: true, detail: 1 }));

    expect(scroll).toHaveBeenCalled();
    expect(document.activeElement).not.toBe(skip);
  });

  it('stops listening once it is gone', async () => {
    const added = vi.spyOn(window, 'addEventListener');
    const removed = vi.spyOn(window, 'removeEventListener');
    const { fixture } = await render();
    const listener = added.mock.calls.find(([type]) => type === 'scroll')![1];

    fixture.destroy();

    expect(removed).toHaveBeenCalledWith('scroll', listener);
  });
});
