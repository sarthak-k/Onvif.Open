import { useMemo } from 'react';

/** The browser's own time zone: a sensible default for a new site. */
export function browserTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  } catch {
    return 'UTC';
  }
}

function allTimeZones(): string[] {
  try {
    return Intl.supportedValuesOf('timeZone');
  } catch {
    return ['UTC'];
  }
}

/** IANA time zones ("Asia/Kolkata"); times at a site are shown in its zone. */
export function TimeZoneField({ label = 'Time zone', value, onChange }: { label?: string; value: string; onChange(value: string): void }) {
  const zones = useMemo(() => {
    const list = allTimeZones();
    return list.includes(value) || !value ? list : [value, ...list];
  }, [value]);

  return (
    <label>
      {label}
      <select value={value} onChange={(e) => onChange(e.target.value)}>
        {zones.map((zone) => (
          <option key={zone} value={zone}>
            {zone.replace(/_/g, ' ')}
          </option>
        ))}
      </select>
    </label>
  );
}
