import type { CameraStatus, Gateway, Site, StoredEvent } from '../../api/types';
import { formatBytes } from '../../shared/format';
import { zoneOffsetMs } from '../playback/time';

/** Something that needs a person: a camera that does not record, a gateway that went quiet, a disk running full. */
export interface Problem {
  id: string;
  severity: 'critical' | 'warning';
  title: string;
  detail?: string | null;
  /** When it started, if known (ms). */
  since?: number | null;
  siteId?: string | null;
  link?: { to: string; label: string };
}

/** Below this much free disk a gateway is about to delete recordings early. */
export const lowDiskBytes = 5 * 1024 ** 3;

/** More files than this waiting for the cloud backup means it does not keep up. */
export const backlogFiles = 100;

const day = 24 * 60 * 60_000;

/** What needs attention, the worst first: cameras not recording, gateways offline, disks and backups. */
export function findProblems(cameras: readonly CameraStatus[], gateways: readonly Gateway[]): Problem[] {
  const problems: Problem[] = [];

  for (const camera of cameras) {
    if (camera.state === 'Reconnecting') {
      problems.push({
        id: `camera-${camera.cameraId}`,
        severity: 'critical',
        title: `${camera.name} is not recording`,
        detail: camera.lastError ?? 'The camera does not answer.',
        since: camera.lastErrorAt ? Date.parse(camera.lastErrorAt) : null,
        siteId: camera.siteId,
        link: { to: `/investigate?camera=${encodeURIComponent(camera.cameraId)}&category=offline&range=7d`, label: 'When it went offline' },
      });
    } else if (camera.state === 'Stopped') {
      problems.push({
        id: `camera-${camera.cameraId}`,
        severity: 'warning',
        title: `${camera.name} is stopped`,
        detail: 'Someone stopped its recording.',
        siteId: camera.siteId,
        link: { to: '/cameras', label: 'Cameras' },
      });
    }
  }

  for (const gateway of gateways) {
    const seen = gateway.lastSeenAt ? Date.parse(gateway.lastSeenAt) : null;
    if (gateway.status === 'Offline') {
      problems.push({
        id: `gateway-${gateway.id}`,
        severity: 'critical',
        title: `Gateway ${gateway.name} is offline`,
        detail: 'Its cameras may not be recording.',
        since: seen,
        link: { to: '/settings/gateways', label: 'Gateways' },
      });
      continue;
    }
    if (gateway.status === 'Waiting') {
      problems.push({ id: `gateway-${gateway.id}`, severity: 'warning', title: `Gateway ${gateway.name} has not reported yet`, link: { to: '/settings/gateways', label: 'Gateways' } });
      continue;
    }

    const health = gateway.health;
    if (health?.freeBytes != null && health.freeBytes < lowDiskBytes) {
      problems.push({
        id: `disk-${gateway.id}`,
        severity: 'warning',
        title: `Disk almost full on ${gateway.name}`,
        detail: `${formatBytes(health.freeBytes)} free: the oldest recordings are deleted early.`,
        link: { to: '/settings/gateways', label: 'Storage' },
      });
    }
    if (health?.backupError) {
      problems.push({ id: `backup-${gateway.id}`, severity: 'warning', title: `Cloud backup of ${gateway.name} fails`, detail: health.backupError, link: { to: '/settings/gateways', label: 'Cloud backup' } });
    } else if ((health?.pendingUploads ?? 0) > backlogFiles) {
      problems.push({
        id: `backup-${gateway.id}`,
        severity: 'warning',
        title: `Cloud backup of ${gateway.name} is behind`,
        detail: `${health!.pendingUploads} files waiting.`,
        link: { to: '/settings/gateways', label: 'Cloud backup' },
      });
    }
  }

  return problems.sort((a, b) => (a.severity === b.severity ? 0 : a.severity === 'critical' ? -1 : 1));
}

/** Midnight that started the day at `ms` in a time zone (ms). */
export function startOfDay(ms: number, timeZone?: string): number {
  const offset = zoneOffsetMs(ms, timeZone);
  return Math.floor((ms + offset) / day) * day - offset;
}

export interface SiteSummary {
  site: Site;
  cameras: number;
  recording: number;
  problems: number;
  /** Events since midnight, site time. */
  eventsToday: StoredEvent[];
}

/** Each site: its cameras, how many record, its problems and today's events (in its own day). */
export function summarizeSites(sites: readonly Site[], cameras: readonly CameraStatus[], problems: readonly Problem[], events: readonly StoredEvent[], now: number): SiteSummary[] {
  return sites.map((site) => {
    const own = cameras.filter((c) => c.siteId === site.id);
    const midnight = startOfDay(now, site.timeZone);
    return {
      site,
      cameras: own.length,
      recording: own.filter((c) => c.state === 'Recording').length,
      problems: problems.filter((p) => p.siteId === site.id).length,
      eventsToday: events.filter((e) => e.siteId === site.id && Date.parse(e.startTime) >= midnight),
    };
  });
}

/** "3 people, 1 vehicle, 12 motion" for the busiest kinds. */
export function describeEvents(events: readonly StoredEvent[], labels: Record<string, string>): string {
  const counts = new Map<string, number>();
  for (const e of events) counts.set(e.category, (counts.get(e.category) ?? 0) + 1);
  return [...counts.entries()]
    .sort((a, b) => b[1] - a[1])
    .slice(0, 3)
    .map(([category, n]) => `${n} ${(labels[category] ?? category).toLowerCase()}`)
    .join(', ');
}
