import type { ScheduleWindow, Weekday } from '../../api/types';
import { weekdays } from './schedule';

/** Weekly hours, in the site's time zone: days and a time range per row. */
export function ScheduleEditor({ schedule, onChange, hint }: { schedule: ScheduleWindow[]; onChange(schedule: ScheduleWindow[]): void; hint: string }) {
  const change = (index: number, window: Partial<ScheduleWindow>) => onChange(schedule.map((w, i) => (i === index ? { ...w, ...window } : w)));
  const toggle = (index: number, day: Weekday) => {
    const days = schedule[index].days;
    change(index, { days: days.includes(day) ? days.filter((d) => d !== day) : [...days, day] });
  };

  return (
    <fieldset className="stack" aria-label="Hours">
      <legend>Hours (in the site's time zone)</legend>
      {schedule.map((window, index) => (
        <div key={index} className="stack schedule-row" role="group" aria-label={`Time range ${index + 1}`}>
          <div className="day-toggles">
            {weekdays.map(({ day, short }) => (
              <label key={day} className="check" title={day}>
                <input type="checkbox" aria-label={day} checked={window.days.includes(day)} onChange={() => toggle(index, day)} />
                {short}
              </label>
            ))}
          </div>
          <div className="row">
            <label>
              From
              <input type="time" value={window.start} onChange={(e) => change(index, { start: e.target.value })} />
            </label>
            <label>
              To
              <input type="time" value={window.end} onChange={(e) => change(index, { end: e.target.value })} />
            </label>
            {schedule.length > 1 && (
              <button type="button" onClick={() => onChange(schedule.filter((_, i) => i !== index))}>
                Remove
              </button>
            )}
          </div>
        </div>
      ))}
      <button type="button" className="link" onClick={() => onChange([...schedule, { days: ['Saturday'], start: '10:00', end: '14:00' }])}>
        Add hours
      </button>
      <p className="muted small">{hint}</p>
    </fieldset>
  );
}
