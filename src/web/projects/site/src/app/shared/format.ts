import { Pipe, PipeTransform } from '@angular/core';

const TZ = 'America/Chicago';

/** Cents to "$300" or "$12.50"; zero reads as "Free". */
@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  transform(cents: number | undefined, freeLabel = 'Free'): string {
    if (cents == null) return '';
    if (cents === 0) return freeLabel;
    const dollars = cents / 100;
    return '$' + (Number.isInteger(dollars) ? dollars.toLocaleString('en-US') : dollars.toFixed(2));
  }
}

/** Formats ISO dates in Chicago time regardless of where the server runs. */
@Pipe({ name: 'when' })
export class WhenPipe implements PipeTransform {
  transform(
    iso: string | undefined,
    style: 'day' | 'mon' | 'dow' | 'time' | 'long' | 'short' | 'range' = 'long',
    end?: string,
  ): string {
    if (!iso) return '';
    const d = new Date(iso.length === 10 ? iso + 'T12:00:00-06:00' : iso);
    const f = (o: Intl.DateTimeFormatOptions) =>
      new Intl.DateTimeFormat('en-US', { timeZone: TZ, ...o }).format(d);
    switch (style) {
      case 'day':
        return f({ day: 'numeric' });
      case 'mon':
        return f({ month: 'short' });
      case 'dow':
        return f({ weekday: 'short' });
      case 'time': {
        const t = (x: Date) =>
          new Intl.DateTimeFormat('en-US', { timeZone: TZ, hour: 'numeric', minute: '2-digit' })
            .format(x)
            .replace(':00', '')
            .replace(' AM', ' am')
            .replace(' PM', ' pm');
        return end ? `${t(d)} – ${t(new Date(end))}` : t(d);
      }
      case 'short':
        return f({ month: 'short', day: 'numeric' });
      case 'range': {
        const e = end ? new Date(end.length === 10 ? end + 'T12:00:00-06:00' : end) : d;
        const sameYear = d.getFullYear() === e.getFullYear();
        const a = new Intl.DateTimeFormat('en-US', {
          timeZone: TZ,
          month: 'short',
          day: 'numeric',
          ...(sameYear ? {} : { year: 'numeric' }),
        }).format(d);
        const b = new Intl.DateTimeFormat('en-US', {
          timeZone: TZ,
          month: 'short',
          day: 'numeric',
          year: 'numeric',
        }).format(e);
        return `${a} – ${b}`;
      }
      default:
        return f({ weekday: 'long', month: 'long', day: 'numeric' });
    }
  }
}

/** "Today" for status checks. Uses the request time on the server and the clock in the browser. */
export function today(): string {
  return new Date().toISOString().slice(0, 10);
}

/** The Chicago calendar date (YYYY-MM-DD) of an instant, so "today" matches the studio's wall clock wherever the server runs. */
export function chicagoDay(at: string | number | Date = Date.now()): string {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: TZ,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date(at));
}

/** The Chicago wall-clock time (HH:MM, 24-hour) of an instant. */
export function chicagoTime(at: string | number | Date = Date.now()): string {
  return new Intl.DateTimeFormat('en-GB', {
    timeZone: TZ,
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }).format(new Date(at));
}

/** A calendar date (YYYY-MM-DD) moved by n days. */
export function addDays(day: string, n: number): string {
  const d = new Date(day + 'T00:00:00Z');
  d.setUTCDate(d.getUTCDate() + n);
  return d.toISOString().slice(0, 10);
}

/** The Sunday (YYYY-MM-DD) that closes the Monday–Sunday week containing a Chicago date. */
export function endOfWeek(day: string): string {
  const d = new Date(day + 'T00:00:00Z');
  d.setUTCDate(d.getUTCDate() + ((7 - d.getUTCDay()) % 7));
  return d.toISOString().slice(0, 10);
}
