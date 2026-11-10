import type { Alert, AlertSummary } from '../../api/types';
import { categoryLabels } from '../playback/events';

/** "Person at Front gate". */
export function describeAlert(alert: Pick<Alert, 'title' | 'cameraName'>): string {
  return alert.cameraName ? `${alert.title} at ${alert.cameraName}` : alert.title;
}

/** The narrowest Investigate range that still holds the alert. */
function rangeFor(ageMs: number): string {
  return ageMs < 50 * 60_000 ? '1h' : ageMs < 23 * 3_600_000 ? '24h' : ageMs < 6.5 * 86_400_000 ? '7d' : '30d';
}

/** Investigate, filtered to the alert's camera and kind of event, around its time. */
export function investigateLink(alert: Alert, now = Date.now()): string {
  const query = new URLSearchParams({ range: rangeFor(now - Date.parse(alert.triggeredAt)) });
  if (alert.siteId) query.set('site', alert.siteId);
  if (alert.cameraId) query.set('camera', alert.cameraId);
  if (alert.category in categoryLabels) query.set('category', alert.category);
  return `/investigate?${query}`;
}

/** Notifications that failed, in words: "Email failed: No mail server is set up." */
export function deliveryProblems(alert: Alert): string[] {
  return Object.entries(alert.delivery ?? {})
    .filter(([, result]) => !result.ok)
    .map(([name, result]) => `${name === 'email' ? 'Email' : name === 'webhook' ? 'Webhook' : name} failed${result.error ? `: ${result.error}` : '.'}`);
}

/**
 * The alert to show a desktop notification for: the newest open one, when it is new since the last check. The first
 * check only remembers it (alerts from before the page was opened are not announced).
 */
export function alertToAnnounce(previousLatestId: string | null | undefined, summary: AlertSummary): Alert | null {
  const latest = summary.latest;
  if (previousLatestId === undefined || !latest || latest.id === previousLatestId) return null;
  return latest;
}
