import type { EventCategory, StoredEvent } from '../../api/types';

/** What each kind of event is called on screen. */
export const categoryLabels: Record<EventCategory, string> = {
  motion: 'Motion',
  person: 'Person',
  vehicle: 'Vehicle',
  object: 'Object',
  intrusion: 'Intrusion',
  tamper: 'Tampering',
  input: 'Input',
  offline: 'Camera offline',
  plugin: 'Plugin',
  other: 'Other',
};

export const categoryOptions = [
  { value: '' as const, label: 'All events' },
  ...(Object.keys(categoryLabels) as EventCategory[]).map((value) => ({ value, label: categoryLabels[value] })),
];

export const startOf = (event: StoredEvent): number => Date.parse(event.startTime);

/** The end, or now for an event still going on. */
export const endOf = (event: StoredEvent, now = Date.now()): number => (event.endTime ? Date.parse(event.endTime) : now);

/** Video shown before and after an event when it is opened. */
export const leadMs = 30_000;
export const trailMs = 60_000;

/** The time window to play for an event: from shortly before it to a minute after (at least two minutes). */
export function windowOf(event: StoredEvent, now = Date.now()): { from: number; to: number } {
  const from = startOf(event) - leadMs;
  const to = Math.min(Math.max(endOf(event, now) + trailMs, from + 2 * 60_000), Math.max(now, from + 60_000));
  return { from, to };
}

export function severityClass(event: StoredEvent): 'info' | 'warning' | 'critical' {
  return event.severity === 'Critical' ? 'critical' : event.severity === 'Warning' ? 'warning' : 'info';
}
