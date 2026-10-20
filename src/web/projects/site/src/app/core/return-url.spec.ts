import { DEFAULT_RETURN_URL, isCmsPath, safeReturnUrl } from './return-url';

describe('safeReturnUrl', () => {
  it.each([
    ['/admin/', '/admin/'],
    ['/admin', '/admin/'],
    ['/admin/classes?draft=1', '/admin/classes?draft=1'],
    ['/admin/pages#seo', '/admin/pages#seo'],
    ['/memberships', '/memberships'],
  ])('keeps the local path %s', (raw, expected) => {
    expect(safeReturnUrl(raw)).toBe(expected);
  });

  it.each([
    [undefined],
    [null],
    [''],
    ['admin'],
    ['https://evil.example/'],
    ['//evil.example/admin'],
    ['/\\evil.example'],
    ['\\\\evil.example'],
    ['/\t/evil.example'],
    ['/\n/evil.example'],
    ['/admin/../..//evil.example'],
    ['/./..//evil.example/admin'],
    ['/%62ff/logout'],
    ['/%73ignin'],
    ['/%E0%A4%A'],
    [['/admin', '/x']],
    [42],
    ['javascript:alert(1)'],
    ['/signin'],
    ['/signin?returnUrl=/admin'],
    ['/SIGNIN'],
    ['/bff/logout'],
    ['/api/v1/cms/pages'],
    ['/' + 'a'.repeat(2048)],
  ])('falls back to the CMS for %j', (raw) => {
    expect(safeReturnUrl(raw)).toBe(DEFAULT_RETURN_URL);
  });

  it('always answers a path on this origin', () => {
    for (const raw of ['/%2F%2Fevil.example', '/admin/../..//evil.example', '/./admin']) {
      const result = safeReturnUrl(raw);
      expect(result.startsWith('/')).toBe(true);
      expect(result.startsWith('//')).toBe(false);
      expect(new URL(result, 'https://6311arts.com').origin).toBe('https://6311arts.com');
    }
  });
});

describe('isCmsPath', () => {
  it.each([
    ['/admin', true],
    ['/admin/', true],
    ['/admin/classes', true],
    ['/administration', false],
    ['/ADMIN/classes', true],
    ['/memberships', false],
  ])('%s → %s', (path, expected) => expect(isCmsPath(path)).toBe(expected));
});
