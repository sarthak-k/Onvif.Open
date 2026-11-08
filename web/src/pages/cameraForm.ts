import { fromSeconds, toSeconds } from '../api/timespan';
import type { CameraConfig, MediaVersion, RecordingMode, Transport } from '../api/types';

/** The editor's state: text for every input, converted to and from the API's camera configuration. */
export interface CameraForm {
  id: string;
  enabled: boolean;
  source: 'onvif' | 'rtsp';
  onvifUrl: string;
  rtspUrl: string;
  username: string;
  /** Empty with hasPassword: keep the stored password. */
  password: string;
  hasPassword: boolean;
  removePassword: boolean;
  profileToken: string;
  transport: Transport;
  mediaVersion: MediaVersion;
  mode: RecordingMode;
  preRollSeconds: string;
  postRollSeconds: string;
  segmentMinutes: string;
  maxSegmentSizeMB: string;
  alignToClockMinutes: string;
  eventsEnabled: boolean;
  triggerTopics: string;
  analyticsEnabled: boolean;
  triggerClasses: string;
  minLikelihood: string;
  analyticsProfileToken: string;
  audioEnabled: boolean;
}

export const emptyCameraForm: CameraForm = {
  id: '',
  enabled: true,
  source: 'onvif',
  onvifUrl: '',
  rtspUrl: '',
  username: '',
  password: '',
  hasPassword: false,
  removePassword: false,
  profileToken: '',
  transport: 'Rtsp',
  mediaVersion: 'Auto',
  mode: 'Continuous',
  preRollSeconds: '5',
  postRollSeconds: '10',
  segmentMinutes: '',
  maxSegmentSizeMB: '',
  alignToClockMinutes: '',
  eventsEnabled: true,
  triggerTopics: '',
  analyticsEnabled: false,
  triggerClasses: '',
  minLikelihood: '0.5',
  analyticsProfileToken: '',
  audioEnabled: false,
};

const text = (value: number | null | undefined, scale = 1) => (value == null ? '' : String(+(value / scale).toFixed(3)));
const list = (value: string) => value.split(/[\n,]/).map((v) => v.trim()).filter(Boolean);
const number = (value: string) => (value.trim() === '' ? null : Number(value));
const span = (value: string, scale = 1) => {
  const n = number(value);
  return n == null || Number.isNaN(n) ? null : fromSeconds(n * scale);
};

export function toForm(camera: CameraConfig): CameraForm {
  return {
    id: camera.id,
    enabled: camera.enabled,
    source: camera.rtspUrl && !camera.onvifUrl ? 'rtsp' : 'onvif',
    onvifUrl: camera.onvifUrl ?? '',
    rtspUrl: camera.rtspUrl ?? '',
    username: camera.username ?? '',
    password: '',
    hasPassword: camera.hasPassword ?? false,
    removePassword: false,
    profileToken: camera.profileToken ?? '',
    transport: camera.transport,
    mediaVersion: camera.mediaVersion,
    mode: camera.mode,
    preRollSeconds: text(toSeconds(camera.preRoll)),
    postRollSeconds: text(toSeconds(camera.postRoll)),
    segmentMinutes: text(toSeconds(camera.rotation?.segmentDuration), 60),
    maxSegmentSizeMB: text(camera.rotation?.maxSegmentSizeMB),
    alignToClockMinutes: text(toSeconds(camera.rotation?.alignToClock), 60),
    eventsEnabled: camera.events?.enabled ?? true,
    triggerTopics: (camera.events?.triggerTopics ?? []).join('\n'),
    analyticsEnabled: camera.analytics?.enabled ?? false,
    triggerClasses: (camera.analytics?.triggerClasses ?? []).join(', '),
    minLikelihood: text(camera.analytics?.minLikelihood ?? 0.5),
    analyticsProfileToken: camera.analytics?.profileToken ?? '',
    audioEnabled: camera.audio?.enabled ?? false,
  };
}

export function toCamera(form: CameraForm): CameraConfig {
  const onvif = form.source === 'onvif';
  const segmentDuration = span(form.segmentMinutes, 60);
  const maxSegmentSizeMB = number(form.maxSegmentSizeMB);
  const alignToClock = span(form.alignToClockMinutes, 60);

  return {
    id: form.id.trim(),
    enabled: form.enabled,
    onvifUrl: onvif ? form.onvifUrl.trim() : null,
    rtspUrl: onvif ? null : form.rtspUrl.trim(),
    username: form.username.trim() || null,
    // null keeps the stored password, "" removes it.
    password: form.removePassword ? '' : form.password || (form.hasPassword ? null : ''),
    profileToken: onvif ? form.profileToken.trim() || null : null,
    transport: form.transport,
    mediaVersion: form.mediaVersion,
    rotation: segmentDuration || maxSegmentSizeMB != null || alignToClock ? { segmentDuration, maxSegmentSizeMB, alignToClock } : null,
    mode: form.mode,
    preRoll: span(form.preRollSeconds) ?? '00:00:05',
    postRoll: span(form.postRollSeconds) ?? '00:00:10',
    events: { enabled: form.eventsEnabled, triggerTopics: list(form.triggerTopics) },
    analytics: {
      enabled: form.analyticsEnabled,
      triggerClasses: list(form.triggerClasses),
      minLikelihood: number(form.minLikelihood) ?? 0.5,
      profileToken: form.analyticsProfileToken.trim() || null,
    },
    audio: { enabled: form.audioEnabled },
  };
}

/** A camera id from a discovered device's name or host: lower case letters, digits and dashes. */
export function suggestId(name: string | null | undefined, url: string | null | undefined, taken: string[]): string {
  let host = '';
  try {
    host = url ? new URL(url).hostname : '';
  } catch {
    // Not a URL.
  }
  const base = (name || host || 'camera').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 40) || 'camera';
  let id = base;
  for (let n = 2; taken.includes(id); n++) id = `${base}-${n}`;
  return id;
}
