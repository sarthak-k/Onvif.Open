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

export function formatClock(ms: number, seconds = true): string {
  return new Date(ms).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: seconds ? '2-digit' : undefined });
}

export function formatDuration(ms: number): string {
  const total = Math.round(ms / 1000);
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  return h ? `${h} h ${pad(m)} min` : m ? `${m} min ${pad(s)} s` : `${s} s`;
}
