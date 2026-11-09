import { useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import type { EventCategory, StoredEvent } from '../../api/types';
import { useCameraStatus } from '../../data/cameras';
import { useEventSearch } from '../../data/recordings';
import { useSites } from '../../data/tenancy';
import { SelectField } from '../../shared/form/fields';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useUser } from '../auth/AuthContext';
import { can } from '../auth/permissions';
import { categoryLabels, categoryOptions, endOf, severityClass, startOf, windowOf } from '../playback/events';
import { formatClock, formatDuration, formatWhen, zoneName } from '../playback/time';
import { ExportButton } from '../exports/ExportButton';
import { SaveClipButton } from './SaveClipButton';
import { SyncedPlayer, type PlayerCamera } from './SyncedPlayer';

const ranges = [
  { value: '1h', label: 'Last hour', ms: 60 * 60_000 },
  { value: '24h', label: 'Last 24 hours', ms: 24 * 60 * 60_000 },
  { value: '7d', label: 'Last 7 days', ms: 7 * 24 * 60 * 60_000 },
  { value: '30d', label: 'Last 30 days', ms: 30 * 24 * 60 * 60_000 },
] as const;

/** At most this many cameras play together. */
const maxCameras = 4;

/** Seconds of video before the event when it is opened. */
const openBeforeMs = 5000;

/**
 * Finds what happened: search the stored events by site, camera, kind and time; open one to play its camera, and
 * others of the site, in sync around it.
 */
export default function InvestigatePage() {
  const [params, setParams] = useSearchParams();
  const siteId = params.get('site') ?? '';
  const cameraId = params.get('camera') ?? '';
  const category = (params.get('category') ?? '') as EventCategory | '';
  const range = ranges.find((r) => r.value === params.get('range')) ?? ranges[1];
  const [refreshed, setRefreshed] = useState(() => Date.now());
  const [selected, setSelected] = useState<StoredEvent | null>(null);
  const [extra, setExtra] = useState<string[]>([]);
  const canSave = can(useUser(), 'cameras.control');
  // Fixed when the event is opened: an event still going on would otherwise move the window at every render.
  const shownWindow = useMemo(() => (selected ? windowOf(selected) : null), [selected]);

  const sites = useSites();
  const statuses = useCameraStatus();
  const cameras = statuses.data ?? [];
  const from = useMemo(() => new Date(refreshed - range.ms).toISOString(), [refreshed, range.ms]);
  const search = useEventSearch({ siteId: siteId || undefined, cameraId: cameraId || undefined, category: category || undefined, from });
  const results = search.data?.pages.flat() ?? [];

  const set = (name: string, value: string) => {
    const next = new URLSearchParams(params);
    if (value) next.set(name, value);
    else next.delete(name);
    if (name === 'site') next.delete('camera');
    setParams(next, { replace: true });
    setSelected(null);
  };

  const siteOptions = [{ value: '', label: 'All sites' }, ...(sites.data ?? []).map((s) => ({ value: s.id, label: s.name }))];
  const cameraOptions = [
    { value: '', label: 'All cameras' },
    ...cameras.filter((c) => !siteId || c.siteId === siteId).map((c) => ({ value: c.cameraId, label: c.name })),
  ];
  const zoneOf = (id: string | null) => sites.data?.find((s) => s.id === id)?.timeZone;
  const nameOf = (id: string) => cameras.find((c) => c.cameraId === id)?.name ?? id;

  const open = (event: StoredEvent) => {
    setSelected(event);
    setExtra([]);
  };
  const neighbours = selected?.cameraId ? cameras.filter((c) => c.cameraId !== selected.cameraId && c.siteId === selected.siteId) : [];
  const playing: PlayerCamera[] = selected?.cameraId
    ? [{ cameraId: selected.cameraId, name: selected.cameraName ?? nameOf(selected.cameraId) }, ...extra.map((id) => ({ cameraId: id, name: nameOf(id) }))]
    : [];

  return (
    <section className="stack">
      <div className="heading">
        <h1>Investigate</h1>
        <button type="button" onClick={() => setRefreshed(Date.now())}>
          Refresh
        </button>
      </div>

      <div className="toolbar" role="search" aria-label="Find events">
        {siteOptions.length > 2 && <SelectField label="Site" value={siteId} options={siteOptions} onChange={(v) => set('site', v)} />}
        <SelectField label="Camera" value={cameraId} options={cameraOptions} onChange={(v) => set('camera', v)} />
        <SelectField label="What" value={category} options={categoryOptions} onChange={(v) => set('category', v)} />
        <SelectField label="When" value={range.value} options={ranges} onChange={(v) => set('range', v)} />
      </div>
      <ErrorMessage error={search.error} />

      <div className="investigate">
        <div className="stack">
          {search.isSuccess && results.length === 0 && <p className="muted">Nothing happened that matches. Try a longer time or all events.</p>}
          <ul className="event-cards" aria-label="Events">
            {results.map((event) => (
              <li key={event.id}>
                <button
                  type="button"
                  className={`event-card ${severityClass(event)}`}
                  aria-pressed={selected?.id === event.id}
                  onClick={() => open(event)}
                >
                  <strong>{categoryLabels[event.category]}</strong>
                  {event.title !== categoryLabels[event.category] && <span>{event.title}</span>}
                  <span className="muted small">
                    {event.cameraName ?? 'No camera'} · {formatWhen(startOf(event), zoneOf(event.siteId))} {zoneName(startOf(event), zoneOf(event.siteId))} · {event.endTime ? formatDuration(endOf(event) - startOf(event)) : 'still going on'}
                  </span>
                </button>
              </li>
            ))}
          </ul>
          {search.hasNextPage && (
            <button type="button" onClick={() => search.fetchNextPage()} disabled={search.isFetchingNextPage}>
              {search.isFetchingNextPage ? 'Loading…' : 'Load more'}
            </button>
          )}
        </div>

        <div className="stack">
          {!selected && <p className="muted">Choose an event to watch what happened.</p>}
          {selected && !selected.cameraId && <p className="muted">This event is not about a camera: there is no video for it.</p>}
          {selected?.cameraId && (
            <>
              <h2>
                {categoryLabels[selected.category]} · {selected.cameraName ?? nameOf(selected.cameraId)}
              </h2>
              {neighbours.length > 0 && (
                <fieldset className="choice" aria-label="Also show">
                  <legend>Also show</legend>
                  {neighbours.map((c) => (
                    <label key={c.cameraId} className="check">
                      <input
                        type="checkbox"
                        checked={extra.includes(c.cameraId)}
                        disabled={!extra.includes(c.cameraId) && extra.length >= maxCameras - 1}
                        onChange={() => setExtra((all) => (all.includes(c.cameraId) ? all.filter((id) => id !== c.cameraId) : [...all, c.cameraId]))}
                      />
                      {c.name}
                    </label>
                  ))}
                </fieldset>
              )}
              <SyncedPlayer
                siteId={selected.siteId}
                from={shownWindow!.from}
                to={shownWindow!.to}
                start={startOf(selected) - openBeforeMs}
                startLabel="the event"
                timeZone={zoneOf(selected.siteId)}
                cameras={playing}
                clipActions={(marks) => (
                  <>
                    <ExportButton cameraIds={playing.map((c) => c.cameraId)} from={marks.from} to={marks.to} />
                    {canSave && <SaveClipButton cameraIds={playing.map((c) => c.cameraId)} marks={marks} eventId={selected.id} />}
                  </>
                )}
                footer={
                  <p className="muted small">
                    {categoryLabels[selected.category]} at {formatClock(startOf(selected), true, zoneOf(selected.siteId))}: the video starts 5 s before it. Click the timeline or an event mark to
                    move all cameras there.
                  </p>
                }
              />
            </>
          )}
        </div>
      </div>
    </section>
  );
}
