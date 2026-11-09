import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { useCameraStatus } from '../../data/cameras';
import { useGateways } from '../../data/gateways';
import { useEventsSince } from '../../data/recordings';
import { useSites } from '../../data/tenancy';
import { useUsers } from '../../data/users';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useUser } from '../auth/AuthContext';
import { can } from '../auth/permissions';
import { categoryLabels, severityClass, startOf } from '../playback/events';
import { formatClock, formatWhen, zoneName } from '../playback/time';
import { describeEvents, findProblems, startOfDay, summarizeSites } from './health';

const checklistKey = 'onvif-open.home.checklist-dismissed';

function readDismissed(): boolean {
  try {
    return localStorage.getItem(checklistKey) === '1';
  } catch {
    return false;
  }
}

/** Since how long, in words: "for 12 min", "for 3 h", "since Mon 9 Nov, 14:02". */
function since(ms: number, now: number, timeZone?: string): string {
  const minutes = Math.round((now - ms) / 60_000);
  if (minutes < 1) return 'just now';
  if (minutes < 60) return `for ${minutes} min`;
  if (minutes < 24 * 60) return `for ${Math.round(minutes / 60)} h`;
  return `since ${formatWhen(ms, timeZone)}`;
}

/**
 * Is everything OK? Problems first (cameras not recording, gateways offline, disks and backups), then each site's
 * health, then what happened today; and, until it is done, what is left to set up.
 */
export default function HomePage() {
  const user = useUser();
  const statuses = useCameraStatus();
  const gateways = useGateways();
  const sites = useSites();
  const [now] = useState(() => Date.now());
  const [dismissed, setDismissed] = useState(readDismissed);

  // Today starts at the earliest midnight of the sites (each site's day is counted in its own time).
  const from = useMemo(() => (sites.data?.length ? Math.min(...sites.data.map((s) => startOfDay(now, s.timeZone))) : null), [sites.data, now]);
  const events = useEventsSince(from);

  const cameras = statuses.data ?? [];
  const problems = findProblems(cameras, gateways.data ?? []);
  const summaries = summarizeSites(sites.data ?? [], cameras, problems, events.data ?? [], now);
  const zoneOf = (id: string | null | undefined) => sites.data?.find((s) => s.id === id)?.timeZone;
  const loaded = statuses.isSuccess && gateways.isSuccess;

  return (
    <section className="stack">
      <div className="heading">
        <h1>Home</h1>
      </div>
      <ErrorMessage error={statuses.error ?? gateways.error ?? events.error} />

      {loaded &&
        (problems.length === 0 ? (
          <p className="alert alert-ok" role="status">
            {cameras.length === 0 ? 'No cameras yet.' : `Everything is recording: ${cameras.length} camera${cameras.length === 1 ? '' : 's'}.`}
          </p>
        ) : (
          <p className="alert alert-error" role="status">
            {problems.length} problem{problems.length === 1 ? ' needs' : 's need'} attention.
          </p>
        ))}

      {problems.length > 0 && (
        <ul className="problems" aria-label="Problems">
          {problems.map((p) => (
            <li key={p.id} className={`card problem ${p.severity}`}>
              <strong>{p.title}</strong>
              {p.since != null && <span className="muted small"> {since(p.since, now, zoneOf(p.siteId))}</span>}
              {p.detail && <div className="small">{p.detail}</div>}
              {p.link && (
                <Link className="small" to={p.link.to}>
                  {p.link.label}
                </Link>
              )}
            </li>
          ))}
        </ul>
      )}

      {can(user, 'cameras.manage') && !dismissed && (
        <Checklist
          cameras={cameras.length}
          backup={!!gateways.data?.some((g) => g.backup?.provider || g.health?.backupProvider)}
          canInvite={can(user, 'users.manage')}
          onDismiss={() => {
            setDismissed(true);
            try {
              localStorage.setItem(checklistKey, '1');
            } catch {
              // Private windows: hidden until the page reloads.
            }
          }}
        />
      )}

      <h2>Sites</h2>
      <ul className="site-cards" aria-label="Sites">
        {summaries.map(({ site, cameras: total, recording, problems: count, eventsToday }) => (
          <li key={site.id} className="card stack" aria-label={site.name}>
            <div className="heading">
              <strong>{site.name}</strong>
              <span className="muted small">
                {formatClock(now, false, site.timeZone)} {zoneName(now, site.timeZone)}
              </span>
            </div>
            <span className={count > 0 ? 'state state-reconnecting' : 'state state-recording'}>
              {total === 0 ? 'No cameras' : `${recording} of ${total} camera${total === 1 ? '' : 's'} recording`}
              {count > 0 && ` · ${count} problem${count === 1 ? '' : 's'}`}
            </span>
            <span className="small">
              {eventsToday.length === 0 ? 'No events today.' : `${eventsToday.length} event${eventsToday.length === 1 ? '' : 's'} today: ${describeEvents(eventsToday, categoryLabels)}.`}
            </span>
            <div className="actions">
              <Link to={`/investigate?site=${encodeURIComponent(site.id)}&range=24h`}>Investigate</Link>
              <Link to="/live">Live</Link>
            </div>
          </li>
        ))}
      </ul>

      <div className="heading">
        <h2>Today</h2>
        <Link to="/investigate?range=24h">All events</Link>
      </div>
      {events.isSuccess && events.data.length === 0 && <p className="muted">Nothing happened today.</p>}
      <ul className="event-cards" aria-label="Today">
        {(events.data ?? []).slice(0, 8).map((e) => (
          <li key={e.id}>
            <Link className={`event-card ${severityClass(e)}`} to={`/investigate?range=24h${e.cameraId ? `&camera=${encodeURIComponent(e.cameraId)}` : ''}&category=${e.category}`}>
              <strong>{categoryLabels[e.category]}</strong>
              {e.title !== categoryLabels[e.category] && <span>{e.title}</span>}
              <span className="muted small">
                {e.cameraName ?? 'No camera'} · {formatClock(startOf(e), true, zoneOf(e.siteId))} {zoneName(startOf(e), zoneOf(e.siteId))}
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </section>
  );
}

/** What is left to set up, until it is done or dismissed. */
function Checklist({ cameras, backup, canInvite, onDismiss }: { cameras: number; backup: boolean; canInvite: boolean; onDismiss(): void }) {
  const users = useUsers(canInvite);
  const steps = [
    { done: cameras > 0, label: 'Add your cameras', to: '/cameras/add' },
    ...(canInvite ? [{ done: (users.data?.length ?? 0) > 1, label: 'Invite the people who watch them', to: '/settings/users' }] : []),
    { done: backup, label: 'Back up recordings to the cloud or a NAS', to: '/settings/gateways' },
    { done: false, label: 'Choose when cameras record (always, around events, business hours)', to: '/settings/policies', optional: true },
  ];
  const left = steps.filter((s) => !s.done && !('optional' in s));
  if (left.length === 0) return null;

  return (
    <section className="card stack" aria-label="Set up">
      <div className="heading">
        <h2>Finish setting up</h2>
        <button type="button" className="link" onClick={onDismiss}>
          Hide
        </button>
      </div>
      <ul className="checklist">
        {steps.map((step) => (
          <li key={step.to} className={step.done ? 'done' : undefined}>
            <span aria-hidden="true">{step.done ? '✓' : '○'}</span>{' '}
            {step.done ? <span className="muted">{step.label}</span> : <Link to={step.to}>{step.label}</Link>}
          </li>
        ))}
      </ul>
    </section>
  );
}
