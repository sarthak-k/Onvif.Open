const pad = (n: number) => String(n).padStart(2, '0');

/** The local day as YYYY-MM-DD (the value of a date input). */
export function dayKey(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** Local midnight of a YYYY-MM-DD day; today when invalid. */
export function dayStart(key: string | null): Date {
  const match = key ? /^(\d{4})-(\d{2})-(\d{2})$/.exec(key) : null;
  const date = match ? new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3])) : new Date();
  date.setHours(0, 0, 0, 0);
  return date;
}

/** The next local midnight (days with a daylight saving change are 23 or 25 hours). */
export function nextDay(start: Date): Date {
  const next = new Date(start);
  next.setDate(next.getDate() + 1);
  return next;
}

/** The time of day, in a site's time zone when given (else the browser's). */
export function formatClock(ms: number, seconds = true, timeZone?: string): string {
  return new Date(ms).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: seconds ? '2-digit' : undefined, timeZone });
}

/** "Mon 9 Nov, 14:02:11", in a site's time zone when given. */
export function formatWhen(ms: number, timeZone?: string): string {
  return new Date(ms).toLocaleString([], { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit', second: '2-digit', timeZone });
}

/** "Mon, 9 Nov 2026", in a site's time zone when given. */
export function formatDay(ms: number, timeZone?: string): string {
  return new Date(ms).toLocaleDateString([], { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric', timeZone });
}

/** The zone's short name at that moment, e.g. "CET" or "GMT+5:30"; empty without a zone. */
export function zoneName(ms: number, timeZone?: string): string {
  if (!timeZone) return '';
  try {
    return new Intl.DateTimeFormat([], { timeZone, timeZoneName: 'short' }).formatToParts(new Date(ms)).find((p) => p.type === 'timeZoneName')?.value ?? timeZone;
  } catch {
    return timeZone;
  }
}

export function formatDuration(ms: number): string {
  const total = Math.round(ms / 1000);
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  return h ? `${h} h ${pad(m)} min` : m ? `${m} min ${pad(s)} s` : `${s} s`;
}

/** How far a time zone is ahead of UTC at that moment, in ms (0 without a zone). */
export function zoneOffsetMs(ms: number, timeZone?: string): number {
  if (!timeZone) return -new Date(ms).getTimezoneOffset() * 60_000;
  try {
    const parts = Object.fromEntries(
      new Intl.DateTimeFormat('en-US', { timeZone, hourCycle: 'h23', year: 'numeric', month: 'numeric', day: 'numeric', hour: 'numeric', minute: 'numeric', second: 'numeric' })
        .formatToParts(new Date(ms))
        .map((p) => [p.type, Number(p.value)]),
    );
    const local = Date.UTC(parts.year, parts.month - 1, parts.day, parts.hour, parts.minute, parts.second);
    return local - Math.floor(ms / 1000) * 1000;
  } catch {
    return 0;
  }
}
