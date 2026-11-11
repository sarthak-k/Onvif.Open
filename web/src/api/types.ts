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
  /** Their authenticator app: Pending while being set up. */
  twoFactor?: TwoFactorState;
  lockedUntil?: string | null;
  lastLoginAt?: string | null;
  createdAt: string;
}

/** The signed-in user, with their organization. */
export interface Me extends User {
  organization: Organization;
  /** The organization requires a second factor and this user has none yet: they set it up before anything else. */
  mustSetUpTwoFactor?: boolean;
  /** Signed in with the organization's identity provider. */
  singleSignOn?: boolean;
  recoveryCodesLeft?: number;
}

export type TwoFactorState = 'Off' | 'Pending' | 'On';

/** An organization's single sign-on (OpenID Connect); the client secret is never sent back. */
export interface SsoSettings {
  enabled: boolean;
  /** OpenID Connect, or SAML 2.0. */
  protocol: 'Oidc' | 'Saml';
  /** SAML: the provider's entity ID, sign-in address and signing certificate (PEM or base64). */
  samlEntityId: string | null;
  samlSsoUrl: string | null;
  samlCertificate: string | null;
  /** SAML: read from the certificate. */
  samlCertificateSubject?: string | null;
  samlCertificateExpires?: string | null;
  issuer: string | null;
  clientId: string | null;
  hasClientSecret: boolean;
  /** Email domains whose people sign in this way. */
  domains: string[];
  /** Accounts are made at the first sign-in (for those domains), with defaultRole. */
  autoCreate: boolean;
  defaultRole: Role;
  /** Members sign in only this way; owners keep their password. */
  required: boolean;
}

export interface SsoRequest extends Omit<SsoSettings, 'hasClientSecret' | 'samlCertificateSubject' | 'samlCertificateExpires'> {
  /** Null keeps the stored secret. */
  clientSecret: string | null;
}

/** What an authenticator app is set up with: a QR code of the URI, or the secret typed in. */
export interface TwoFactorSetup {
  secret: string;
  uri: string;
}

/** The answer to a password: tokens, or a challenge to send back with the app's code. */
export interface SignInStep {
  twoFactorRequired: true;
  challenge: string;
  expiresIn: number;
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
  /** Every member signs in with a second factor. */
  requireTwoFactor?: boolean;
}

export interface SetupStatus {
  required: boolean;
  tokenRequired: boolean;
  regions: { id: Region; name: string }[];
}

/** A cloud deployment: the region it keeps data in, and where the others are. Not the cloud: enabled is false. */
export interface CloudInfo {
  enabled: boolean;
  region: Region | null;
  /** Whether people can sign up here themselves. */
  signup: boolean;
  /** Whether organizations can have a gateway run for them in the cloud (cameras on the internet). */
  cloudGateways: boolean;
  regions: { id: Region; name: string; webUrl: string }[] | null;
}

/** Self-service sign-up: the account is the email, verified by a link before anything exists. */
export interface SignupRequest {
  email: string;
  password: string;
  organizationName: string;
  siteName: string;
  timeZone: string;
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

export type PolicyMode = 'Always' | 'EventsOnly' | 'Schedule';

export type Weekday = 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday' | 'Sunday';

/** Weekly hours in the site's time zone; an end not after the start runs past midnight. */
export interface ScheduleWindow {
  days: Weekday[];
  /** "HH:mm". */
  start: string;
  end: string;
}

export interface RecordingPolicy {
  id: string;
  name: string;
  mode: PolicyMode;
  preRollSeconds: number;
  postRollSeconds: number;
  continuousRetentionDays: number;
  eventRetentionDays: number;
  isDefault: boolean;
  /** When a "Schedule" policy records all the time. */
  schedule: ScheduleWindow[];
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
  /** ONVIF: a smaller stream for live grids (opened only while watched); none: the main stream. */
  subProfileToken?: string | null;
  /** RTSP cameras: a smaller stream for live grids. */
  subRtspUrl?: string | null;
  /** The gateway that records it (this server's when absent); fixed once added. */
  gatewayId?: string | null;
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
  /** An ONVIF camera: it may pan, tilt and zoom. */
  onvif?: boolean;
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
  /** The streams the recorder would use (ONVIF): the sharpest to record, the smallest for tiles. */
  recommended?: { mainProfile?: string | null; subProfile?: string | null } | null;
  /** A picture from the camera as a data URL, when asked for. */
  snapshot?: string | null;
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
  /** A gateway the cloud runs for this organization: where starting it stands; null for the others. */
  hosting: GatewayHosting | null;
}

export interface GatewayHosting {
  state: 'Starting' | 'Running' | 'Failed';
  /** Why it could not be started (Failed). */
  error: string | null;
  changedAt: string | null;
}

export interface GatewayRequest {
  name?: string;
  storage?: StorageLimits;
  /** Empty provider: no backup. Settings null: keep the stored ones. */
  backup?: { provider: string; prefix: string | null; keepLocalCopyHours: number; settings: Record<string, string> | null };
  discovery?: { multicast: boolean; addresses: string[] };
}

export interface RecordingSegment {
  cameraId: string;
  fileName: string;
  startTime: string;
  lastWriteTime: string;
  size: number;
  isActive: boolean;
  isLocal: boolean;
  isArchived: boolean;
}

export type EventCategory = 'motion' | 'person' | 'vehicle' | 'object' | 'intrusion' | 'tamper' | 'input' | 'offline' | 'plugin' | 'other';

/** Something a camera, a detection, the recorder or a plugin reported (stored, searchable). */
export interface StoredEvent {
  id: string;
  cameraId: string | null;
  cameraName: string | null;
  siteId: string | null;
  category: EventCategory;
  severity: 'Info' | 'Warning' | 'Critical';
  source: string;
  title: string;
  topic: string | null;
  startTime: string;
  /** Null while it is still going on. */
  endTime: string | null;
  data: unknown;
}

export interface EventQuery {
  cameraId?: string;
  siteId?: string;
  category?: EventCategory;
  from?: string;
  to?: string;
  /** Only events that started before this time (the next page). */
  before?: string;
  limit?: number;
}

export type CaseStatus = 'Open' | 'Closed';

/** A bookmark in a case: a stretch of time on cameras of one site, with a note. */
export interface CaseClip {
  id: string;
  siteId: string | null;
  cameraIds: string[];
  cameras: { id: string; name: string }[];
  from: string;
  to: string;
  note: string | null;
  eventId: string | null;
  createdBy: string | null;
  createdAt: string;
}

/** An incident being looked into; the video of its clips is kept while it is open. */
export interface Case {
  id: string;
  name: string;
  description: string | null;
  status: CaseStatus;
  createdBy: string | null;
  createdAt: string;
  updatedAt: string;
  closedAt: string | null;
  clipCount: number | null;
  /** Only when reading one case: the clips of sites the user sees. */
  clips: CaseClip[] | null;
}

export interface CaseRequest {
  name?: string;
  description?: string | null;
  status?: CaseStatus;
}

export interface ClipRequest {
  cameraIds: string[];
  from: string;
  to: string;
  note: string | null;
  eventId: string | null;
}

export type ExportStatus = 'Queued' | 'Running' | 'Ready' | 'Failed' | 'Expired';

/** A watermarked MP4 of one camera, kept for 7 days. */
export interface VideoExport {
  id: string;
  cameraId: string;
  cameraName: string | null;
  siteId: string | null;
  caseId: string | null;
  from: string;
  to: string;
  status: ExportStatus;
  error: string | null;
  size: number | null;
  /** SHA-256 of the file, to show it was not changed. */
  sha256: string | null;
  requestedBy: string | null;
  createdAt: string;
  completedAt: string | null;
  expiresAt: string;
  downloadable: boolean;
}

export interface ShareLink {
  id: string;
  /** Only right after it was made: the link's path (with its token). */
  path: string | null;
  createdBy: string | null;
  createdAt: string;
  expiresAt: string;
  revokedAt: string | null;
  downloads: number;
}

export type Severity = 'Info' | 'Warning' | 'Critical';
export type AlertKind = 'Event' | 'Offline';

/** When to raise an alert and whom to tell. Empty lists mean "any". */
export interface AlertRule {
  id: string;
  name: string;
  enabled: boolean;
  kind: AlertKind;
  /** Event rules: the categories that alert; none: every category. */
  categories: EventCategory[];
  minSeverity: Severity | null;
  /** Offline rules: how long a camera is offline before it alerts. */
  offlineMinutes: number;
  siteIds: string[];
  cameraIds: string[];
  /** When it alerts, in the site's time zone; none: at any time. */
  hours: ScheduleWindow[];
  /** A rule alerts at most once per camera in this time. */
  cooldownMinutes: number;
  emails: string[];
  webhookUrl: string | null;
  hasWebhookSecret: boolean;
  createdAt: string;
  updatedAt: string;
}

/** Every field is replaced, except `webhookSecret`: null keeps it, "" removes it. */
export interface AlertRuleRequest {
  name: string;
  enabled: boolean;
  kind: AlertKind;
  categories: EventCategory[];
  minSeverity: Severity | null;
  offlineMinutes: number;
  siteIds: string[];
  cameraIds: string[];
  hours: ScheduleWindow[];
  cooldownMinutes: number;
  emails: string[];
  webhookUrl: string | null;
  webhookSecret: string | null;
}

export interface DeliveryResult {
  ok: boolean;
  error: string | null;
}

/** An alert in the inbox. */
export interface Alert {
  id: string;
  ruleId: string | null;
  ruleName: string | null;
  siteId: string | null;
  cameraId: string | null;
  cameraName: string | null;
  eventId: string | null;
  category: string;
  severity: Severity;
  title: string;
  /** See docs/STATE-MACHINES.md (Alert). */
  status: 'Open' | 'Acknowledged';
  triggeredAt: string;
  /** Null while it is open. */
  acknowledgedAt: string | null;
  acknowledgedBy: string | null;
  /** How each notification went, by "email" and "webhook". */
  delivery: Record<string, DeliveryResult>;
}

export interface AlertSummary {
  open: number;
  latest: Alert | null;
}

/** Pan and tilt from -1 to 1, zoom from 0 (wide) to 1; as speeds for a move, -1 to 1 each. */
export interface PtzVector {
  pan?: number | null;
  tilt?: number | null;
  zoom?: number | null;
}

export interface PtzStatus {
  position: PtzVector | null;
  isMoving: boolean;
  error: string | null;
}

export interface PtzPreset {
  token: string;
  name: string;
  position: PtzVector | null;
}
