import type { CameraConfig, CameraStatus } from '../../api/types';
import { describeSource, describeState, mergeCameras } from './cameraRows';

const config = (id: string, name: string, siteId = 's1', extra: Partial<CameraConfig> = {}) => ({ id, name, siteId, enabled: true, ...extra }) as CameraConfig;
const status = (cameraId: string, name: string, state: string, siteId = 's1') => ({ cameraId, name, state, siteId }) as CameraStatus;

describe('camera rows', () => {
  it('merges configuration and status by id, sorted by site and name', () => {
    const sites = new Map([
      ['s1', 'Main St'],
      ['s2', 'Airport'],
    ]);
    const rows = mergeCameras([config('id-b', 'Back door'), config('id-a', 'Gate', 's2')], [status('id-b', 'Back door', 'Recording'), status('id-c', 'Lobby', 'Stopped')], sites);

    expect(rows.map((r) => [r.name, !!r.config, r.status?.state])).toEqual([
      ['Gate', true, undefined],
      ['Back door', true, 'Recording'],
      ['Lobby', false, 'Stopped'],
    ]);
  });

  it('describes the source and the state', () => {
    expect(describeSource(config('a', 'A', 's1', { onvifUrl: 'http://10.0.0.5/onvif/device_service' }))).toBe('ONVIF 10.0.0.5');
    expect(describeSource(config('a', 'A', 's1', { rtspUrl: 'rtsp://10.0.0.6:8554/s' }))).toBe('RTSP 10.0.0.6:8554');
    expect(describeState({ id: 'a', name: 'A', config: config('a', 'A', 's1', { enabled: false }) })).toBe('Disabled');
    expect(describeState({ id: 'a', name: 'A', config: config('a', 'A') })).toBe('Starting');
  });
});
