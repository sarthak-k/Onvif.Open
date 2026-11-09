// Shapes of the recorder's REST API (camelCase JSON, enums as strings, TimeSpans as "[d.]hh:mm:ss").

export type Role = 'Viewer' | 'Operator' | 'Admin' | 'Owner';
export const roles: readonly Role[] = ['Viewer', 'Operator', 'Admin', 'Owner'];

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
  /** Site ids the user may see; empty: all sites. */
  sites: string[];
  enabled: boolean;
  mustChangePassword: boolean;
  lockedUntil?: string | null;
  lastLoginAt?: string | null;
  createdAt: string;
}

/** The signed-in user, with their organization. */
export interface Me extends User {
  organization: Organization;
}

export interface UserRequest {
  userName?: string;
  password?: string;
  role?: Role;
  enabled?: boolean;
  /** Empty: all sites; absent on a change: unchanged. */
  sites?: string[];
}

export type Region = 'asia' | 'us';

export interface Organization {
  id: string;
  name: string;
  region: Region;
}

export interface SetupStatus {
  required: boolean;
  tokenRequired: boolean;
  regions: { id: Region; name: string }[];
}

export interface SetupRequest {
  organizationName: string;
  region: Region;
  siteName: string;
  timeZone: string;
  userName: string;
  password: string;
  setupToken?: string;
}

export interface Site {
  id: string;
  name: string;
  /** IANA time zone, e.g. "Asia/Kolkata". */
  timeZone: string;
  address?: string | null;
}

export type SiteRequest = Omit<Site, 'id'>;

export type PolicyMode = 'Always' | 'EventsOnly';

export interface RecordingPolicy {
  id: string;
  name: string;
  mode: PolicyMode;
  preRollSeconds: number;
  postRollSeconds: number;
  continuousRetentionDays: number;
  eventRetentionDays: number;
  isDefault: boolean;
}

export type RecordingPolicyRequest = Omit<RecordingPolicy, 'id'>;

export type Transport = 'Rtsp' | 'Udp';
export type MediaVersion = 'Auto' | 'Media1' | 'Media2';
export type RecordingMode = 'Continuous' | 'Triggered';

export interface CameraConfig {
  /** Assigned by the recorder; absent when adding. */
  id?: string;
  name: string;
  siteId: string;
  /** Absent: the organization's default policy. */
  policyId?: string | null;
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
  /** Set by the recorder from the policy (read-only). */
  mode?: RecordingMode;
  events: { enabled: boolean; triggerTopics: string[] };
  analytics: { enabled: boolean; triggerClasses: string[]; minLikelihood: number; profileToken?: string | null };
  audio: { enabled: boolean };
}

export interface CameraStatus {
  cameraId: string;
  name: string;
  siteId?: string | null;
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
  configuredCameraName?: string | null;
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

/** Something a person did, as the audit log records it. */
export interface AuditEntry {
  id: string;
  time: string;
  userId?: string | null;
  userName?: string | null;
  /** "area.verb", e.g. "camera.update" or "auth.signin_failed". */
  action: string;
  targetType?: string | null;
  targetId?: string | null;
  targetName?: string | null;
  /** What the recorder recorded about the action: usually an object of names and values. */
  details?: unknown;
  ipAddress?: string | null;
}

export interface AuditQuery {
  /** An action ("camera.update") or an area ("camera"). */
  action?: string;
  userId?: string;
  /** Only entries older than this time (the next page). */
  before?: string;
  limit?: number;
}

/** Disk space limits in GB; null: no limit. */
export interface StorageLimits {
  minFreeSpaceGB: number | null;
  maxSizePerCameraGB: number | null;
  maxTotalSizeGB: number | null;
}

export interface GatewayHealth {
  cameras: number;
  recording: number;
  /** Cameras that should record but cannot (reconnecting). */
  failing: number;
  freeBytes: number | null;
  recordedBytes: number;
  backupProvider: string | null;
  pendingUploads: number | null;
  backupError: string | null;
}

/** What records a site's cameras: today the recorder itself (embedded). */
export interface Gateway {
  id: string;
  name: string;
  location: 'Local' | 'Cloud';
  embedded: boolean;
  /** Online: reported in the last two minutes; Waiting: never reported yet. */
  status: 'Online' | 'Offline' | 'Waiting';
  version: string | null;
  startedAt: string | null;
  lastSeenAt: string | null;
  health: GatewayHealth | null;
  /** A saved backup change applies when the recorder restarts. */
  restartRequired: boolean;
  /** The settings: only for administrators who see every site (null otherwise). */
  storage: StorageLimits | null;
  backup: { provider: string | null; prefix: string | null; keepLocalCopyHours: number; settings: string[] } | null;
  discovery: { multicast: boolean; addresses: string[] } | null;
}

export interface GatewayRequest {
  name?: string;
  storage?: StorageLimits;
  /** Empty provider: no backup. Settings null: keep the stored ones. */
  backup?: { provider: string; prefix: string | null; keepLocalCopyHours: number; settings: Record<string, string> | null };
  discovery?: { multicast: boolean; addresses: string[] };
}
