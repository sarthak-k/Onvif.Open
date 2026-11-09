import type { ApiClient } from './client';
import type { CameraConfig, CameraStatus, DiscoveredDevice, AuditEntry, AuditQuery, Layout, LayoutRequest, Me, Organization, ProbeResult, RecordingPolicy, RecordingPolicyRequest, SetupStatus, Site, SiteRequest, User, UserRequest } from './types';

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
  test(camera: Partial<CameraConfig>): Promise<ProbeResult>;
  discover(): Promise<DiscoveredDevice[]>;
}

export interface RecordingApi {
  status(): Promise<CameraStatus[]>;
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

export interface AuditApi {
  /** Newest first. */
  list(query?: AuditQuery): Promise<AuditEntry[]>;
}

/** Where players fetch media, and the credentials they send. */
export interface MediaApi {
  /** Live HLS playlist of a camera (absolute URL). */
  liveUrl(cameraId: string): string;
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
      test: (camera) => client.post('/api/config/cameras/test', camera),
      discover: () => client.get('/api/discovery'),
    },
    recording: {
      status: () => client.get('/api/cameras'),
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
      token: () => client.token,
    },
    users: {
      list: () => client.get('/api/users'),
      create: (user) => client.post('/api/users', user),
      update: (id, user) => client.put(`/api/users/${segment(id)}`, user),
      remove: (id) => client.delete(`/api/users/${segment(id)}`),
    },
    audit: {
      list: (query = {}) => client.get(`/api/audit${queryString({ ...query })}`),
    },
  };
}
