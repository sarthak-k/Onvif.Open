import type { CameraConfig, CameraStatus } from '../../api/types';

/** One camera in the list: its configuration (administrators) and recording status (everyone). */
export interface CameraRow {
  id: string;
  config?: CameraConfig;
  status?: CameraStatus;
}

export function mergeCameras(configs: CameraConfig[] | undefined, statuses: CameraStatus[] | undefined): CameraRow[] {
  const rows = new Map<string, CameraRow>();
  configs?.forEach((config) => rows.set(config.id, { id: config.id, config }));
  statuses?.forEach((status) => rows.set(status.cameraId, { ...rows.get(status.cameraId), id: status.cameraId, status }));
  return [...rows.values()].sort((a, b) => a.id.localeCompare(b.id));
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
