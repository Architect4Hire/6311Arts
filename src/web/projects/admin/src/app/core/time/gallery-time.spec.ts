import { formatDateRange, instantOf, wallClockOf } from './gallery-time';

describe('wallClockOf', () => {
  it('reads a UTC instant on the gallery clock, daylight and standard time alike', () => {
    expect(wallClockOf('2026-10-24T23:00:00+00:00')).toEqual({ date: '2026-10-24', time: '18:00' });
    expect(wallClockOf('2026-12-12T00:30:00+00:00')).toEqual({ date: '2026-12-11', time: '18:30' });
  });

  it('refuses what is not an instant', () => {
    expect(wallClockOf('soon')).toBeNull();
  });
});

describe('instantOf', () => {
  it('turns a gallery wall-clock time into the UTC instant the API keeps', () => {
    expect(instantOf('2026-10-24', '18:00')).toBe('2026-10-24T23:00:00+00:00');
    expect(instantOf('2026-12-12', '18:00')).toBe('2026-12-13T00:00:00+00:00');
  });

  it('round-trips with wallClockOf on either side of a change of clocks', () => {
    for (const [date, time] of [
      ['2026-03-07', '23:15'],
      ['2026-03-08', '04:00'],
      ['2026-10-31', '22:00'],
      ['2026-11-01', '12:00'],
    ] as const) {
      expect(wallClockOf(instantOf(date, time)!)).toEqual({ date, time });
    }
  });

  it('takes the first of an hour the autumn change repeats, and moves a skipped spring time forward', () => {
    // 1:30 on 1 November 2026 happens twice; the daylight one is 06:30 UTC.
    expect(instantOf('2026-11-01', '01:30')).toBe('2026-11-01T06:30:00+00:00');
    // 2:30 on 8 March 2026 never happens; a wall clock reads 3:30 at that moment.
    expect(wallClockOf(instantOf('2026-03-08', '02:30')!)).toEqual({
      date: '2026-03-08',
      time: '03:30',
    });
  });

  it('refuses a malformed date or time', () => {
    expect(instantOf('24/10/2026', '18:00')).toBeNull();
    expect(instantOf('2026-10-24', '6pm')).toBeNull();
  });
});

describe('formatDateRange', () => {
  // ICU spaces a range with thin spaces; the words are what matter here.
  const plain = (text: string) => text.replace(/\s/g, ' ');

  it('writes a run as one range, unmoved by any time zone', () => {
    expect(plain(formatDateRange('2026-10-24', '2026-12-05'))).toBe('Oct 24 – Dec 5, 2026');
    expect(plain(formatDateRange('2026-12-12', '2027-01-30'))).toBe('Dec 12, 2026 – Jan 30, 2027');
    expect(plain(formatDateRange('2026-10-24', '2026-10-24'))).toBe('Oct 24, 2026');
  });
});
