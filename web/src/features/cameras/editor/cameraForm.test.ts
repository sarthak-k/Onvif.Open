import type { CameraConfig } from '../../../api/types';
import { emptyCameraForm, suggestName, toCamera, toForm } from './cameraForm';

const stored: CameraConfig = {
  id: '6f0c2b1e-0000-4000-8000-000000000001',
  name: 'Front door',
  siteId: 'site-main',
  policyId: 'p-events',
  enabled: true,
  onvifUrl: 'http://10.0.0.5/onvif/device_service',
  rtspUrl: null,
  username: 'admin',
  hasPassword: true,
  profileToken: 'main',
  subProfileToken: 'sub',
  subRtspUrl: null,
  gatewayId: null,
  transport: 'Udp',
  mediaVersion: 'Media2',
  rotation: { segmentDuration: '00:05:00', maxSegmentSizeMB: 256, alignToClock: null },
  events: { enabled: true, triggerTopics: ['tns1:RuleEngine/CellMotionDetector/Motion'] },
  analytics: { enabled: true, triggerClasses: ['Human', 'Vehicle'], minLikelihood: 0.7, profileToken: null },
  audio: { enabled: true },
};

describe('camera form', () => {
  it('round-trips a stored camera and keeps its password', () => {
    const camera = toCamera(toForm(stored));
    const { hasPassword: _, id: __, ...expected } = stored;
    expect(camera).toEqual({ ...expected, password: null });
  });

  it('removes the password when asked', () => {
    expect(toCamera({ ...toForm(stored), removePassword: true }).password).toBe('');
  });

  it('sends a new password', () => {
    expect(toCamera({ ...toForm(stored), password: 'new' }).password).toBe('new');
  });

  it('builds an RTSP camera with the default policy', () => {
    const camera = toCamera({ ...emptyCameraForm, name: ' Gate ', siteId: 'site-main', source: 'rtsp', rtspUrl: 'rtsp://10.0.0.6/s1', profileToken: 'ignored' });
    expect(camera).toMatchObject({ name: 'Gate', siteId: 'site-main', policyId: null, onvifUrl: null, rtspUrl: 'rtsp://10.0.0.6/s1', profileToken: null, rotation: null, password: '' });
  });

  it('suggests a free name', () => {
    expect(suggestName('Front Door', null, [])).toBe('Front Door');
    expect(suggestName(null, 'http://192.168.1.20/onvif/device_service', [])).toBe('192.168.1.20');
    expect(suggestName('Cam', null, ['cam', 'Cam 2'])).toBe('Cam 3');
  });
});
