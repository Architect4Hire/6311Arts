/**
 * Wall-clock time at the gallery. The API sends and keeps instants in UTC (`+00:00`) and says which zone to show
 * them in (`displayTimeZone`, api-contract.md); staff think in the gallery's own clock. These convert between the
 * two with `Intl` and an explicit IANA zone, never the browser's own.
 */

/** `GalleryTimeZone.Id`: the zone the API names when it sends none. */
export const GALLERY_TIME_ZONE = 'America/Chicago';

/** A date (`YYYY-MM-DD`) and time (`HH:mm`, 24-hour) as a clock in the zone reads. */
export interface WallClock {
  readonly date: string;
  readonly time: string;
}

const formatters = new Map<string, Intl.DateTimeFormat>();

function partsFormatter(zone: string): Intl.DateTimeFormat {
  let formatter = formatters.get(zone);
  if (!formatter) {
    formatter = new Intl.DateTimeFormat('en-US', {
      timeZone: zone,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      hourCycle: 'h23',
    });
    formatters.set(zone, formatter);
  }
  return formatter;
}

/** The zone's wall clock at `ms`, written as if it were UTC: comparing it with `ms` gives the zone's offset. */
function wallAsUtc(ms: number, zone: string): number {
  const parts: Record<string, number> = {};
  for (const part of partsFormatter(zone).formatToParts(new Date(ms))) {
    if (part.type !== 'literal') parts[part.type] = Number(part.value);
  }
  return Date.UTC(parts['year'], parts['month'] - 1, parts['day'], parts['hour'], parts['minute']);
}

const pad = (value: number) => String(value).padStart(2, '0');

/** What a clock at the gallery reads at `iso`. `null` for a string that is not an instant. */
export function wallClockOf(iso: string, zone = GALLERY_TIME_ZONE): WallClock | null {
  const ms = Date.parse(iso);
  if (Number.isNaN(ms)) return null;
  const wall = new Date(wallAsUtc(ms, zone));
  return {
    date: `${wall.getUTCFullYear()}-${pad(wall.getUTCMonth() + 1)}-${pad(wall.getUTCDate())}`,
    time: `${pad(wall.getUTCHours())}:${pad(wall.getUTCMinutes())}`,
  };
}

/**
 * The instant a clock at the gallery reads `date` `time`, as the API writes instants (`…+00:00`). `null` when either
 * is not a date or time.
 *
 * Twice a year the clock is not one-to-one. In the autumn hour that happens twice, the first (daylight) one is
 * meant. A spring time the clocks skip moves forward by the gap, as a wall clock would: 2:30 becomes 3:30.
 */
export function instantOf(date: string, time: string, zone = GALLERY_TIME_ZONE): string | null {
  const d = /^(\d{4})-(\d{2})-(\d{2})$/.exec(date);
  const t = /^(\d{2}):(\d{2})$/.exec(time);
  if (!d || !t) return null;

  const target = Date.UTC(Number(d[1]), Number(d[2]) - 1, Number(d[3]), Number(t[1]), Number(t[2]));
  if (Number.isNaN(target)) return null;

  const firstOffset = wallAsUtc(target, zone) - target;
  const first = target - firstOffset;
  const secondOffset = wallAsUtc(first, zone) - first;
  const second = target - secondOffset;
  const instant =
    secondOffset === firstOffset || wallAsUtc(second, zone) !== target ? first : second;

  return new Date(instant).toISOString().replace(/\.\d{3}Z$/, '+00:00');
}

const RANGE = new Intl.DateTimeFormat('en-US', {
  month: 'short',
  day: 'numeric',
  year: 'numeric',
  timeZone: 'UTC',
});

/** A show's run, `YYYY-MM-DD` to `YYYY-MM-DD`, as "Oct 24 – Dec 5, 2026". Calendar dates: no zone moves them. */
export function formatDateRange(start: string, end: string): string {
  const from = Date.parse(`${start}T00:00:00Z`);
  const to = Date.parse(`${end}T00:00:00Z`);
  if (Number.isNaN(from) || Number.isNaN(to)) return '—';
  return from <= to ? RANGE.formatRange(from, to) : RANGE.format(from);
}
