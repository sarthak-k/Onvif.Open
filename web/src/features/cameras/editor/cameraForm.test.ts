import type { CameraConfig } from '../../../api/types';
import { emptyCameraForm, suggestId, toCamera, toForm } from './cameraForm';

const stored: CameraConfig = {
  id: 'front',
  enabled: true,
  onvifUrl: 'http://10.0.0.5/onvif/device_service',
  rtspUrl: null,
  username: 'admin',
  hasPassword: true,
  profileToken: 'main',
  transport: 'Udp',
  mediaVersion: 'Media2',
  rotation: { segmentDuration: '00:05:00', maxSegmentSizeMB: 256, alignToClock: null },
  mode: 'Triggered',
  preRoll: '00:00:03',
  postRoll: '00:00:20',
  events: { enabled: true, triggerTopics: ['tns1:RuleEngine/CellMotionDetector/Motion'] },
  analytics: { enabled: true, triggerClasses: ['Human', 'Vehicle'], minLikelihood: 0.7, profileToken: null },
  audio: { enabled: true },
};

describe('camera form', () => {
  it('round-trips a stored camera and keeps its password', () => {
    const camera = toCamera(toForm(stored));
    const { hasPassword: _, ...expected } = stored;
    expect(camera).toEqual({ ...expected, password: null });
  });

  it('removes the password when asked', () => {
    expect(toCamera({ ...toForm(stored), removePassword: true }).password).toBe('');
  });

  it('sends a new password', () => {
    expect(toCamera({ ...toForm(stored), password: 'new' }).password).toBe('new');
  });

  it('builds an RTSP camera with recorder defaults', () => {
    const camera = toCamera({ ...emptyCameraForm, id: ' gate ', source: 'rtsp', rtspUrl: 'rtsp://10.0.0.6/s1', profileToken: 'ignored' });
    expect(camera).toMatchObject({ id: 'gate', onvifUrl: null, rtspUrl: 'rtsp://10.0.0.6/s1', profileToken: null, rotation: null, password: '', preRoll: '00:00:05', postRoll: '00:00:10' });
  });

  it('suggests a free id', () => {
    expect(suggestId('Front Door (Hikvision)', null, [])).toBe('front-door-hikvision');
    expect(suggestId(null, 'http://192.168.1.20/onvif/device_service', [])).toBe('192-168-1-20');
    expect(suggestId('Cam', null, ['cam', 'cam-2'])).toBe('cam-3');
  });
});
