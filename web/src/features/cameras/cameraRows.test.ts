import type { CameraConfig, CameraStatus } from '../../api/types';
import { describeSource, describeState, mergeCameras } from './cameraRows';

const config = (id: string, extra: Partial<CameraConfig> = {}) => ({ id, enabled: true, ...extra }) as CameraConfig;
const status = (cameraId: string, state: string) => ({ cameraId, state }) as CameraStatus;

describe('camera rows', () => {
  it('merges configuration and status by id, sorted', () => {
    const rows = mergeCameras([config('b'), config('a')], [status('b', 'Recording'), status('c', 'Stopped')]);
    expect(rows.map((r) => [r.id, !!r.config, r.status?.state])).toEqual([
      ['a', true, undefined],
      ['b', true, 'Recording'],
      ['c', false, 'Stopped'],
    ]);
  });

  it('describes the source and the state', () => {
    expect(describeSource(config('a', { onvifUrl: 'http://10.0.0.5/onvif/device_service' }))).toBe('ONVIF 10.0.0.5');
    expect(describeSource(config('a', { rtspUrl: 'rtsp://10.0.0.6:8554/s' }))).toBe('RTSP 10.0.0.6:8554');
    expect(describeState({ id: 'a', config: config('a', { enabled: false }) })).toBe('Disabled');
    expect(describeState({ id: 'a', config: config('a') })).toBe('Starting');
  });
});
