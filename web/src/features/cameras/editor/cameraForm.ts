import { fromSeconds, toSeconds } from '../../../api/timespan';
import type { FieldSetter } from '../../../shared/form/useForm';
import type { CameraConfig, MediaVersion, Transport } from '../../../api/types';

/** The editor's state: text for every input, converted to and from the API's camera configuration. */
export interface CameraForm {
  name: string;
  siteId: string;
  /** Empty: the organization's default policy. */
  policyId: string;
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
  subProfileToken: string;
  subRtspUrl: string;
  /** Empty: this server's own gateway. */
  gatewayId: string;
  transport: Transport;
  mediaVersion: MediaVersion;
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
  name: '',
  siteId: '',
  policyId: '',
  enabled: true,
  source: 'onvif',
  onvifUrl: '',
  rtspUrl: '',
  username: '',
  password: '',
  hasPassword: false,
  removePassword: false,
  profileToken: '',
  subProfileToken: '',
  subRtspUrl: '',
  gatewayId: '',
  transport: 'Rtsp',
  mediaVersion: 'Auto',
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
    name: camera.name,
    siteId: camera.siteId,
    policyId: camera.policyId ?? '',
    enabled: camera.enabled,
    source: camera.rtspUrl && !camera.onvifUrl ? 'rtsp' : 'onvif',
    onvifUrl: camera.onvifUrl ?? '',
    rtspUrl: camera.rtspUrl ?? '',
    username: camera.username ?? '',
    password: '',
    hasPassword: camera.hasPassword ?? false,
    removePassword: false,
    profileToken: camera.profileToken ?? '',
    subProfileToken: camera.subProfileToken ?? '',
    subRtspUrl: camera.subRtspUrl ?? '',
    gatewayId: camera.gatewayId ?? '',
    transport: camera.transport,
    mediaVersion: camera.mediaVersion,
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
    name: form.name.trim(),
    siteId: form.siteId,
    policyId: form.policyId || null,
    enabled: form.enabled,
    onvifUrl: onvif ? form.onvifUrl.trim() : null,
    rtspUrl: onvif ? null : form.rtspUrl.trim(),
    username: form.username.trim() || null,
    // null keeps the stored password, "" removes it.
    password: form.removePassword ? '' : form.password || (form.hasPassword ? null : ''),
    profileToken: onvif ? form.profileToken.trim() || null : null,
    subProfileToken: onvif ? form.subProfileToken.trim() || null : null,
    subRtspUrl: onvif ? null : form.subRtspUrl.trim() || null,
    gatewayId: form.gatewayId || null,
    transport: form.transport,
    mediaVersion: form.mediaVersion,
    rotation: segmentDuration || maxSegmentSizeMB != null || alignToClock ? { segmentDuration, maxSegmentSizeMB, alignToClock } : null,
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

/** A name for a discovered device: its own name, else its host; numbered when already taken. */
export function suggestName(name: string | null | undefined, url: string | null | undefined, taken: string[]): string {
  let host = '';
  try {
    host = url ? new URL(url).hostname : '';
  } catch {
    // Not a URL.
  }
  const base = (name || host || 'Camera').trim().slice(0, 90);
  let candidate = base;
  for (let n = 2; taken.some((t) => t.toLowerCase() === candidate.toLowerCase()); n++) candidate = `${base} ${n}`;
  return candidate;
}

/** Props of each editor section: the values and the setter. */
export interface SectionProps {
  form: CameraForm;
  set: FieldSetter<CameraForm>;
}
