import { decodeProblem } from './problem';
import { decodeSession } from './session';

describe('decodeSession', () => {
  it('accepts exactly the gateway shape', () => {
    expect(decodeSession({ displayName: 'Ada', roles: ['Admin'] })).toEqual({
      displayName: 'Ada',
      roles: ['Admin'],
    });
  });

  it.each([null, 'Ada', {}, { displayName: 'Ada' }, { displayName: 'Ada', roles: [1] }])(
    'refuses %j',
    (body) => expect(decodeSession(body)).toBeNull(),
  );
});

describe('decodeProblem', () => {
  it('reads the gateway and API envelope', () => {
    expect(
      decodeProblem(
        { status: 409, title: 'Conflict', code: 'concurrency.conflict', correlationId: 'a1' },
        409,
      ),
    ).toEqual({
      status: 409,
      title: 'Conflict',
      code: 'concurrency.conflict',
      correlationId: 'a1',
    });
  });

  it('falls back to the response status and treats an empty correlation id as none', () => {
    expect(decodeProblem({ title: 'Nope', correlationId: '' }, 502)).toEqual({
      status: 502,
      title: 'Nope',
      code: null,
      correlationId: null,
    });
  });

  it('is null for a body that is not ProblemDetails', () => {
    expect(decodeProblem('<html>', 500)).toBeNull();
  });
});
