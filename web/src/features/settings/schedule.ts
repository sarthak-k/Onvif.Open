import type { ScheduleWindow, Weekday } from '../../api/types';

export const weekdays: readonly { day: Weekday; short: string }[] = [
  { day: 'Monday', short: 'Mon' },
  { day: 'Tuesday', short: 'Tue' },
  { day: 'Wednesday', short: 'Wed' },
  { day: 'Thursday', short: 'Thu' },
  { day: 'Friday', short: 'Fri' },
  { day: 'Saturday', short: 'Sat' },
  { day: 'Sunday', short: 'Sun' },
];

export const businessHours: ScheduleWindow[] = [{ days: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'], start: '08:00', end: '18:00' }];

/** "Mon–Fri", "Mon, Wed, Sat", "Every day". */
export function describeDays(days: readonly Weekday[]): string {
  const indexes = weekdays.map((w, i) => (days.includes(w.day) ? i : -1)).filter((i) => i >= 0);
  if (indexes.length === 7) {
    return 'Every day';
  }
  const runs: number[][] = [];
  for (const i of indexes) {
    const last = runs[runs.length - 1];
    if (last && last[last.length - 1] === i - 1) last.push(i);
    else runs.push([i]);
  }
  return runs
    .map((run) => (run.length >= 3 ? `${weekdays[run[0]].short}–${weekdays[run[run.length - 1]].short}` : run.map((i) => weekdays[i].short).join(', ')))
    .join(', ');
}

/** "Mon–Fri 08:00–18:00; Sat 10:00–13:00". */
export function describeSchedule(schedule: readonly ScheduleWindow[]): string {
  return schedule.map((w) => `${describeDays(w.days)} ${w.start}–${w.end}${w.end <= w.start ? ' (next day)' : ''}`).join('; ');
}

/** Problems the recorder would refuse, in the same words, so the dialog can say so before saving. */
export function scheduleProblem(schedule: readonly ScheduleWindow[]): string | null {
  if (schedule.length === 0) return 'Add at least one time range.';
  if (schedule.some((w) => w.days.length === 0)) return 'Each time range needs at least one day.';
  if (schedule.some((w) => !w.start || !w.end)) return 'Each time range needs a start and an end.';
  if (schedule.some((w) => w.start === w.end)) return 'A time range cannot start and end at the same time.';
  return null;
}
