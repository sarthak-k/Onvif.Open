import type { Option } from '../../../shared/form/fields';
import type { MediaVersion, RecordingMode, Transport } from '../../../api/types';
import type { CameraForm } from './cameraForm';

// The editor's choices as data: a new transport, media service or mode is one line here.

export const sourceOptions: readonly Option<CameraForm['source']>[] = [
  { value: 'onvif', label: 'ONVIF device' },
  { value: 'rtsp', label: 'RTSP stream only' },
];

export const transportOptions: readonly Option<Transport>[] = [
  { value: 'Rtsp', label: 'RTP over RTSP (TCP)' },
  { value: 'Udp', label: 'RTP over UDP' },
];

export const mediaVersionOptions: readonly Option<MediaVersion>[] = [
  { value: 'Auto', label: 'Automatic' },
  { value: 'Media1', label: 'Media (ver10)' },
  { value: 'Media2', label: 'Media2 (ver20, H.265)' },
];

export const modeOptions: readonly Option<RecordingMode>[] = [
  { value: 'Continuous', label: 'Continuous' },
  { value: 'Triggered', label: 'On events and detections only' },
];

export const capabilityLabels: Record<string, string> = {
  ptz: 'PTZ',
  imaging: 'Imaging',
  analytics: 'Analytics',
  replay: 'Recordings on the camera',
  deviceIo: 'Inputs and relays',
};
