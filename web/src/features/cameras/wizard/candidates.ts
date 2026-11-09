import type { CameraConfig, DiscoveredDevice, ProbeProfile, ProbeResult } from '../../../api/types';
import { emptyCameraForm, suggestName, toCamera } from '../editor/cameraForm';

/** Where a camera stands in the wizard. */
export type CandidateState = 'untested' | 'testing' | 'ok' | 'unauthorized' | 'unreachable';

/** A camera the wizard may add: found on the network or typed in, then tested, named and given a policy. */
export interface Candidate {
  key: string;
  onvifUrl: string;
  /** What the camera calls itself (discovery), or its address. */
  label: string;
  hardware?: string | null;
  selected: boolean;
  /** Its own sign-in instead of the shared one (when the shared one is refused). */
  ownCredentials: boolean;
  username: string;
  password: string;
  state: CandidateState;
  error?: string | null;
  snapshot?: string | null;
  profiles: ProbeProfile[];
  name: string;
  siteId: string;
  policyId: string;
  /** The stream to record; the recommended one unless changed under Advanced. */
  profileToken: string;
  addedId?: string;
  addError?: string;
}

export interface SharedCredentials {
  username: string;
  password: string;
}

function newCandidate(onvifUrl: string, label: string, hardware: string | null | undefined, taken: string[]): Candidate {
  return {
    key: onvifUrl,
    onvifUrl,
    label,
    hardware,
    selected: true,
    ownCredentials: false,
    username: '',
    password: '',
    state: 'untested',
    profiles: [],
    name: suggestName(label, onvifUrl, taken),
    siteId: '',
    policyId: '',
    profileToken: '',
  };
}

export function fromDevice(device: DiscoveredDevice, taken: string[]): Candidate | null {
  if (!device.deviceServiceUri) {
    return null;
  }
  return newCandidate(device.deviceServiceUri, device.name || hostOf(device.deviceServiceUri), device.hardware, taken);
}

/** "10.0.5.20", "10.0.5.20:8080" or a full device service URL. */
export function fromAddress(address: string, taken: string[]): Candidate | null {
  const text = address.trim();
  if (!text) {
    return null;
  }
  const url = /^https?:\/\//i.test(text) ? text : `http://${text}/onvif/device_service`;
  try {
    new URL(url);
  } catch {
    return null;
  }
  return newCandidate(url, hostOf(url), null, taken);
}

function hostOf(url: string): string {
  try {
    return new URL(url).host;
  } catch {
    return url;
  }
}

export function credentialsOf(candidate: Candidate, shared: SharedCredentials): SharedCredentials {
  return candidate.ownCredentials ? { username: candidate.username, password: candidate.password } : shared;
}

/** The candidate after a test: its state, picture and streams (the recommended one selected). */
export function withProbe(candidate: Candidate, result: ProbeResult): Candidate {
  if (!result.ok) {
    return { ...candidate, state: result.kind === 'unauthorized' ? 'unauthorized' : 'unreachable', error: result.error };
  }
  return {
    ...candidate,
    state: 'ok',
    error: null,
    snapshot: result.snapshot,
    profiles: result.profiles ?? [],
    profileToken: candidate.profileToken || result.recommended?.mainProfile || '',
  };
}

export function toCameraConfig(candidate: Candidate, shared: SharedCredentials): CameraConfig {
  const { username, password } = credentialsOf(candidate, shared);
  return toCamera({
    ...emptyCameraForm,
    name: candidate.name.trim(),
    siteId: candidate.siteId,
    policyId: candidate.policyId,
    source: 'onvif',
    onvifUrl: candidate.onvifUrl,
    username,
    password,
    profileToken: candidate.profileToken,
  });
}

/** "1920×1080 H264, 25 fps". */
export function describeProfile(profile: ProbeProfile): string {
  const size = profile.width && profile.height ? `${profile.width}×${profile.height}` : profile.name || profile.token;
  const parts = [size, profile.encoding, profile.frameRate ? `${profile.frameRate} fps` : null].filter(Boolean);
  return parts.join(' ').replace(/ (\d+ fps)$/, ', $1');
}

export const stateLabels: Record<CandidateState, string> = {
  untested: 'Not tested yet',
  testing: 'Testing…',
  ok: 'Ready',
  unauthorized: 'Wrong user name or password',
  unreachable: 'Cannot be reached',
};

/** Runs <code>work</code> on every item, at most <code>limit</code> at a time. */
export async function eachLimited<T>(items: readonly T[], limit: number, work: (item: T) => Promise<void>): Promise<void> {
  let next = 0;
  const runners = Array.from({ length: Math.min(limit, items.length) }, async () => {
    while (next < items.length) {
      await work(items[next++]);
    }
  });
  await Promise.all(runners);
}
