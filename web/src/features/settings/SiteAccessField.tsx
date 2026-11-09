import type { Site } from '../../api/types';

/** Which sites a user may see: all (an empty list), or the ones ticked. */
export function SiteAccessField({ sites, value, onChange }: { sites: readonly Site[]; value: string[]; onChange(value: string[]): void }) {
  const all = value.length === 0;

  return (
    <fieldset className="choice column">
      <legend>Sites</legend>
      <label className="check">
        <input type="checkbox" checked={all} onChange={(e) => onChange(e.target.checked ? [] : sites.slice(0, 1).map((s) => s.id))} />
        All sites
      </label>
      {!all &&
        sites.map((site) => (
          <label key={site.id} className="check">
            <input
              type="checkbox"
              checked={value.includes(site.id)}
              onChange={(e) => {
                const next = e.target.checked ? [...value, site.id] : value.filter((id) => id !== site.id);
                // Unticking the last site means "all sites": keep at least one.
                if (next.length) onChange(next);
              }}
            />
            {site.name}
          </label>
        ))}
    </fieldset>
  );
}

export function describeSites(siteIds: readonly string[], sites: readonly Site[]): string {
  if (siteIds.length === 0) return 'All sites';
  return siteIds.map((id) => sites.find((s) => s.id === id)?.name ?? 'Removed site').join(', ');
}
