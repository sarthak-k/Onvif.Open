import type { CameraConfig, CameraStatus } from '../../api/types';

/** One camera in the list: its configuration (administrators) and recording status (everyone). */
export interface CameraRow {
  id: string;
  name: string;
  siteId?: string | null;
  config?: CameraConfig;
  status?: CameraStatus;
}

/** Configuration (administrators) and status (everyone) of each camera, by site name, then camera name. */
export function mergeCameras(configs: CameraConfig[] | undefined, statuses: CameraStatus[] | undefined, siteNames: ReadonlyMap<string, string> = new Map()): CameraRow[] {
  const rows = new Map<string, CameraRow>();
  configs?.forEach((config) => config.id && rows.set(config.id, { id: config.id, name: config.name, siteId: config.siteId, config }));
  statuses?.forEach((status) => {
    const row = rows.get(status.cameraId);
    rows.set(status.cameraId, { id: status.cameraId, name: row?.name ?? status.name, siteId: row?.siteId ?? status.siteId, config: row?.config, status });
  });
  const site = (row: CameraRow) => (row.siteId ? (siteNames.get(row.siteId) ?? '') : '');
  return [...rows.values()].sort((a, b) => site(a).localeCompare(site(b)) || a.name.localeCompare(b.name));
}

export function describeSource(config: CameraConfig | undefined): string {
  const url = config?.onvifUrl || config?.rtspUrl;
  if (!url) return '—';
  try {
    return `${config?.onvifUrl ? 'ONVIF' : 'RTSP'} ${new URL(url).host}`;
  } catch {
    return url;
  }
}

export function describeState(row: CameraRow): string {
  return row.status?.state ?? (row.config?.enabled === false ? 'Disabled' : 'Starting');
}
