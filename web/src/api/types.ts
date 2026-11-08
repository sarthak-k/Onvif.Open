// Shapes of the recorder's REST API (camelCase JSON, enums as strings, TimeSpans as "[d.]hh:mm:ss").

export type Role = 'Viewer' | 'Operator' | 'Admin';
export const roles: readonly Role[] = ['Viewer', 'Operator', 'Admin'];

export interface Tokens {
  tokenType: string;
  accessToken: string;
  expiresIn: number;
  refreshToken: string;
}

export interface User {
  id: string;
  userName: string;
  role: Role;
  enabled: boolean;
  mustChangePassword: boolean;
  lockedUntil?: string | null;
  lastLoginAt?: string | null;
  createdAt: string;
}

export interface UserRequest {
  userName?: string;
  password?: string;
  role?: Role;
  enabled?: boolean;
}

export type Transport = 'Rtsp' | 'Udp';
export type MediaVersion = 'Auto' | 'Media1' | 'Media2';
export type RecordingMode = 'Continuous' | 'Triggered';

export interface CameraConfig {
  id: string;
  enabled: boolean;
  onvifUrl?: string | null;
  rtspUrl?: string | null;
  username?: string | null;
  /** Write-only: null or absent keeps the stored password, "" removes it. */
  password?: string | null;
  hasPassword?: boolean;
  profileToken?: string | null;
  transport: Transport;
  mediaVersion: MediaVersion;
  rotation?: { segmentDuration?: string | null; maxSegmentSizeMB?: number | null; alignToClock?: string | null } | null;
  mode: RecordingMode;
  preRoll: string;
  postRoll: string;
  events: { enabled: boolean; triggerTopics: string[] };
  analytics: { enabled: boolean; triggerClasses: string[]; minLikelihood: number; profileToken?: string | null };
  audio: { enabled: boolean };
}

export interface CameraStatus {
  cameraId: string;
  state: string;
  mode: string;
  isTriggered: boolean;
  codec: string;
  audioCodec?: string | null;
  recordsAudio: boolean;
  transport?: string | null;
  currentSegment?: string | null;
  framesRecorded: number;
  bytesRecorded: number;
  segmentsCompleted: number;
  lastFrameAt?: string | null;
  lastError?: string | null;
  lastErrorAt?: string | null;
}

export interface DiscoveredDevice {
  endpointReference?: string | null;
  name?: string | null;
  hardware?: string | null;
  location?: string | null;
  deviceServiceUri?: string | null;
  serviceAddresses: string[];
  respondedFrom?: string | null;
  configuredCameraId?: string | null;
}

export interface ProbeProfile {
  token: string;
  name?: string | null;
  encoding?: string | null;
  width?: number | null;
  height?: number | null;
  frameRate?: number | null;
  bitrate?: number | null;
  ptz: boolean;
  analytics: boolean;
}

export interface ProbeResult {
  ok: boolean;
  kind?: 'unauthorized' | 'unreachable' | null;
  error?: string | null;
  device?: { manufacturer?: string | null; model?: string | null; firmwareVersion?: string | null; serialNumber?: string | null } | null;
  profiles?: ProbeProfile[] | null;
  capabilities?: { ptz: boolean; imaging: boolean; analytics: boolean; replay: boolean; deviceIo: boolean } | null;
  stream?: { codec?: string | null; audio?: string | null } | null;
}

export interface Layout {
  id: string;
  name: string;
  /** Grid template id (see features/live/gridTemplates.ts), e.g. "2x2" or "1+5". */
  grid: string;
  /** Camera id per tile, in the template's order; null: empty tile. */
  cameras: (string | null)[];
  shared: boolean;
  /** Whether the signed-in user may change or delete it. */
  editable: boolean;
  updatedAt: string;
}

export interface LayoutRequest {
  name: string;
  grid: string;
  cameras: (string | null)[];
  /** Only read when creating; administrators only. */
  shared?: boolean;
}
