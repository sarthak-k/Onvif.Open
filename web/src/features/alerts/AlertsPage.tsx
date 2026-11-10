import { useState } from 'react';
import { Link } from 'react-router-dom';
import type { Alert } from '../../api/types';
import { useAcknowledge, useAlerts } from '../../data/alerts';
import { useSites } from '../../data/tenancy';
import { SelectField } from '../../shared/form/fields';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useUser } from '../auth/AuthContext';
import { can } from '../auth/permissions';
import { formatWhen } from '../playback/time';
import { deliveryProblems, describeAlert, investigateLink } from './alerts';
import { useNotificationPermission } from './useDesktopNotifications';

const statusOptions = [
  { value: 'open' as const, label: 'Open alerts' },
  { value: '' as const, label: 'All alerts' },
];

const severityClass = (alert: Alert) => (alert.severity === 'Critical' ? 'critical' : alert.severity === 'Warning' ? 'warning' : 'info');

/** What the alert rules caught: newest first; operators acknowledge what they have seen to. */
export default function AlertsPage() {
  const [status, setStatus] = useState<'open' | ''>('open');
  const alerts = useAlerts(status || undefined);
  const acknowledge = useAcknowledge();
  const user = useUser();
  const canAcknowledge = can(user, 'cameras.control');
  const sites = useSites();
  const zoneOf = (id: string | null) => sites.data?.find((s) => s.id === id)?.timeZone;
  const siteName = (id: string | null) => sites.data?.find((s) => s.id === id)?.name;
  const [permission, requestPermission] = useNotificationPermission();

  return (
    <section className="stack">
      <div className="heading">
        <h1>Alerts</h1>
        {can(user, 'settings.manage') && <Link to="/settings/alerts">Alert rules</Link>}
      </div>
      <p className="muted">What your alert rules caught. Acknowledge an alert once someone has seen to it.</p>
      {permission === 'default' && (
        <p className="card">
          Get a desktop notification for each new alert while Onvif.Open is open.{' '}
          <button type="button" className="link" onClick={requestPermission}>
            Turn on desktop notifications
          </button>
        </p>
      )}
      <div className="toolbar">
        <SelectField<'open' | ''> label="Show" value={status} options={statusOptions} onChange={setStatus} />
      </div>
      <ErrorMessage error={alerts.error ?? acknowledge.error} />

      {alerts.isSuccess && alerts.data.length === 0 && (
        <p className="muted">{status === 'open' ? 'No open alerts.' : 'No alerts yet.'} {can(user, 'settings.manage') && <Link to="/settings/alerts">Set up alert rules</Link>}</p>
      )}
      <ul className="event-cards" aria-label="Alerts">
        {(alerts.data ?? []).map((alert) => {
          const problems = deliveryProblems(alert);
          return (
            <li key={alert.id} className={`event-card ${severityClass(alert)}`} aria-label={describeAlert(alert)}>
              <div className="heading">
                <strong>{describeAlert(alert)}</strong>
                <span className="muted small">{formatWhen(Date.parse(alert.triggeredAt), zoneOf(alert.siteId))}</span>
              </div>
              <span className="small">
                {[siteName(alert.siteId), alert.ruleName && `Rule: ${alert.ruleName}`, alert.severity].filter(Boolean).join(' · ')}
              </span>
              {problems.map((problem) => (
                <span key={problem} className="small text-warn">
                  {problem}
                </span>
              ))}
              <div className="actions">
                <Link to={investigateLink(alert)}>Investigate</Link>
                {alert.status === 'Acknowledged' && alert.acknowledgedAt ? (
                  <span className="muted small">
                    Acknowledged{alert.acknowledgedBy ? ` by ${alert.acknowledgedBy}` : ''} {formatWhen(Date.parse(alert.acknowledgedAt), zoneOf(alert.siteId))}
                  </span>
                ) : (
                  canAcknowledge && (
                    <button type="button" disabled={acknowledge.isPending} onClick={() => acknowledge.mutate(alert.id)}>
                      Acknowledge
                    </button>
                  )
                )}
              </div>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
