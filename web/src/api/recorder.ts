import type { ApiClient } from './client';
import type { CameraConfig, CameraStatus, DiscoveredDevice, AuditEntry, AuditQuery, Case, CaseClip, CaseRequest, CaseStatus, ClipRequest, EventQuery, RecordingSegment, StoredEvent, Gateway, GatewayRequest, Layout, LayoutRequest, Me, Organization, ProbeResult, RecordingPolicy, RecordingPolicyRequest, SetupStatus, Site, SiteRequest, User, UserRequest } from './types';

// The recorder's API by area. Pages depend on these interfaces, not on HTTP, so tests and later back ends can
// provide their own implementation (dependency inversion, interface segregation).

export interface AuthApi {
  me(): Promise<Me>;
}

export interface SetupApi {
  /** Whether the recorder still needs its first owner (no sign-in needed). */
  status(): Promise<SetupStatus>;
}

export interface OrganizationApi {
  get(): Promise<Organization>;
  rename(name: string): Promise<Organization>;
}

export interface SitesApi {
  list(): Promise<Site[]>;
  create(site: SiteRequest): Promise<Site>;
  update(siteId: string, site: SiteRequest): Promise<Site>;
  remove(siteId: string): Promise<void>;
}

export interface PoliciesApi {
  list(): Promise<RecordingPolicy[]>;
  create(policy: RecordingPolicyRequest): Promise<RecordingPolicy>;
  update(policyId: string, policy: Partial<RecordingPolicyRequest>): Promise<RecordingPolicy>;
  remove(policyId: string): Promise<void>;
}

export interface CamerasApi {
  list(): Promise<CameraConfig[]>;
  get(cameraId: string): Promise<CameraConfig>;
  create(camera: CameraConfig): Promise<CameraConfig>;
  update(cameraId: string, camera: CameraConfig): Promise<CameraConfig>;
  remove(cameraId: string): Promise<void>;
  /** Connects to the camera without saving it. A null password with an id uses the stored one. */
  test(camera: Partial<CameraConfig>, options?: { snapshot?: boolean }): Promise<ProbeResult>;
  discover(): Promise<DiscoveredDevice[]>;
}

export interface RecordingApi {
  status(): Promise<CameraStatus[]>;
  /** Recorded files overlapping [from, to]. */
  segments(cameraId: string, from: Date, to: Date): Promise<RecordingSegment[]>;
  start(cameraId: string): Promise<void>;
  stop(cameraId: string): Promise<void>;
}

export interface UsersApi {
  list(): Promise<User[]>;
  create(user: UserRequest): Promise<User>;
  update(userId: string, user: UserRequest): Promise<User>;
  remove(userId: string): Promise<void>;
}

export interface LayoutsApi {
  list(): Promise<Layout[]>;
  create(layout: LayoutRequest): Promise<Layout>;
  update(layoutId: string, layout: LayoutRequest): Promise<Layout>;
  remove(layoutId: string): Promise<void>;
}

export interface GatewaysApi {
  list(): Promise<Gateway[]>;
  update(gatewayId: string, gateway: GatewayRequest): Promise<Gateway>;
  /** Pairs the gateway that shows this 6-digit code; null name: the one it gave. */
  pair(code: string, name: string | null): Promise<Gateway>;
  remove(gatewayId: string): Promise<void>;
  /** The address a new gateway is set up with ("ControlPlane:Url"): this recorder's. */
  controlPlaneUrl(): string;
}

export interface CasesApi {
  /** Most recently changed first; all statuses when none is given. */
  list(status?: CaseStatus): Promise<Case[]>;
  get(caseId: string): Promise<Case>;
  create(request: CaseRequest): Promise<Case>;
  update(caseId: string, request: CaseRequest): Promise<Case>;
  remove(caseId: string): Promise<void>;
  addClip(caseId: string, clip: ClipRequest): Promise<CaseClip>;
  updateClip(caseId: string, clipId: string, note: string | null): Promise<CaseClip>;
  removeClip(caseId: string, clipId: string): Promise<void>;
}

export interface EventsApi {
  /** Newest first. */
  list(query?: EventQuery): Promise<StoredEvent[]>;
}

export interface AuditApi {
  /** Newest first. */
  list(query?: AuditQuery): Promise<AuditEntry[]>;
}

/** Where players fetch media, and the credentials they send. */
export interface MediaApi {
  /** Live HLS playlist of a camera (absolute URL). */
  liveUrl(cameraId: string): string;
  /** HLS playlist of the recordings from the file holding `from` up to `to` (absolute URL). */
  recordingsUrl(cameraId: string, from: Date, to: Date): string;
  /** The current access token, read at each request so renewals are picked up. */
  token(): string | null;
}

export interface RecorderApi {
  auth: AuthApi;
  setup: SetupApi;
  organization: OrganizationApi;
  sites: SitesApi;
  policies: PoliciesApi;
  cameras: CamerasApi;
  recording: RecordingApi;
  users: UsersApi;
  layouts: LayoutsApi;
  media: MediaApi;
  audit: AuditApi;
  gateways: GatewaysApi;
  events: EventsApi;
  cases: CasesApi;
}

const segment = encodeURIComponent;

/** "?a=1&b=2" of the values that are set, or "". */
function queryString(values: Record<string, string | number | undefined>): string {
  const query = new URLSearchParams();
  for (const [name, value] of Object.entries(values)) {
    if (value !== undefined && value !== '') {
      query.set(name, String(value));
    }
  }
  const text = query.toString();
  return text ? `?${text}` : '';
}

export function createRecorderApi(client: ApiClient): RecorderApi {
  return {
    auth: {
      me: () => client.get('/api/auth/me'),
    },
    setup: {
      status: () => client.anonymous('GET', '/api/setup'),
    },
    organization: {
      get: () => client.get('/api/organization'),
      rename: (name) => client.put('/api/organization', { name }),
    },
    sites: {
      list: () => client.get('/api/sites'),
      create: (site) => client.post('/api/sites', site),
      update: (id, site) => client.put(`/api/sites/${segment(id)}`, site),
      remove: (id) => client.delete(`/api/sites/${segment(id)}`),
    },
    policies: {
      list: () => client.get('/api/recording-policies'),
      create: (policy) => client.post('/api/recording-policies', policy),
      update: (id, policy) => client.put(`/api/recording-policies/${segment(id)}`, policy),
      remove: (id) => client.delete(`/api/recording-policies/${segment(id)}`),
    },
    cameras: {
      list: () => client.get('/api/config/cameras'),
      get: (id) => client.get(`/api/config/cameras/${segment(id)}`),
      create: (camera) => client.post('/api/config/cameras', camera),
      update: (id, camera) => client.put(`/api/config/cameras/${segment(id)}`, camera),
      remove: (id) => client.delete(`/api/config/cameras/${segment(id)}`),
      test: (camera, options) => client.post(`/api/config/cameras/test${options?.snapshot ? '?snapshot=true' : ''}`, camera),
      discover: () => client.get('/api/discovery'),
    },
    recording: {
      status: () => client.get('/api/cameras'),
      segments: (id, from, to) => client.get(`/api/cameras/${segment(id)}/segments${queryString({ from: from.toISOString(), to: to.toISOString() })}`),
      start: (id) => client.post(`/api/cameras/${segment(id)}/start`),
      stop: (id) => client.post(`/api/cameras/${segment(id)}/stop`),
    },
    layouts: {
      list: () => client.get('/api/layouts'),
      create: (layout) => client.post('/api/layouts', layout),
      update: (id, layout) => client.put(`/api/layouts/${segment(id)}`, layout),
      remove: (id) => client.delete(`/api/layouts/${segment(id)}`),
    },
    media: {
      liveUrl: (id) => new URL(`${client.baseUrl}/api/cameras/${segment(id)}/live.m3u8`, window.location.origin).toString(),
      recordingsUrl: (id, from, to) =>
        new URL(`${client.baseUrl}/api/cameras/${segment(id)}/recordings.m3u8${queryString({ from: from.toISOString(), to: to.toISOString() })}`, window.location.origin).toString(),
      token: () => client.token,
    },
    users: {
      list: () => client.get('/api/users'),
      create: (user) => client.post('/api/users', user),
      update: (id, user) => client.put(`/api/users/${segment(id)}`, user),
      remove: (id) => client.delete(`/api/users/${segment(id)}`),
    },
    events: {
      list: (query = {}) => client.get(`/api/events${queryString({ ...query })}`),
    },
    cases: {
      list: (status) => client.get(`/api/cases${queryString({ status })}`),
      get: (id) => client.get(`/api/cases/${segment(id)}`),
      create: (request) => client.post('/api/cases', request),
      update: (id, request) => client.put(`/api/cases/${segment(id)}`, request),
      remove: (id) => client.delete(`/api/cases/${segment(id)}`),
      addClip: (id, clip) => client.post(`/api/cases/${segment(id)}/clips`, clip),
      updateClip: (id, clipId, note) => client.put(`/api/cases/${segment(id)}/clips/${segment(clipId)}`, { note }),
      removeClip: (id, clipId) => client.delete(`/api/cases/${segment(id)}/clips/${segment(clipId)}`),
    },
    audit: {
      list: (query = {}) => client.get(`/api/audit${queryString({ ...query })}`),
    },
    gateways: {
      list: () => client.get('/api/gateways'),
      update: (id, gateway) => client.put(`/api/gateways/${segment(id)}`, gateway),
      pair: (code, name) => client.post('/api/gateways/pair', { code, name }),
      remove: (id) => client.delete(`/api/gateways/${segment(id)}`),
      controlPlaneUrl: () => new URL(client.baseUrl || '/', window.location.origin).toString().replace(/\/$/, ''),
    },
  };
}
