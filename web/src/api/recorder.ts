import type { ApiClient } from './client';
import type { CameraConfig, CameraEvent, CameraStatus, DiscoveredDevice, Layout, LayoutRequest, PluginEvent, ProbeResult, RecordingSegment, User, UserRequest } from './types';

// The recorder's API by area. Pages depend on these interfaces, not on HTTP, so tests and later back ends can
// provide their own implementation (dependency inversion, interface segregation).

export interface AuthApi {
  me(): Promise<User>;
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

export interface EventsApi {
  /** Camera events in [from, to), newest first. */
  camera(cameraId: string, from: Date, to: Date): Promise<CameraEvent[]>;
  /** Plugin events in [from, to), newest first. */
  plugin(cameraId: string, from: Date, to: Date): Promise<PluginEvent[]>;
}

/** Where players fetch media, and the credentials they send. */
export interface MediaApi {
  /** Live HLS playlist of a camera (absolute URL). */
  liveUrl(cameraId: string): string;
  /** HLS playlist of the recordings from the segment holding `from` up to `to`. */
  recordingsUrl(cameraId: string, from: Date, to: Date): string;
  /** A download of the recordings between from and to, cut into one file (carries the token: build it just before use). */
  clipDownloadUrl(cameraId: string, from: Date, to: Date): string;
  /** The current access token, read at each request so renewals are picked up. */
  token(): string | null;
}

export interface RecorderApi {
  auth: AuthApi;
  cameras: CamerasApi;
  recording: RecordingApi;
  users: UsersApi;
  layouts: LayoutsApi;
  events: EventsApi;
  media: MediaApi;
}

const segment = encodeURIComponent;
const range = (from: Date, to: Date) => `from=${encodeURIComponent(from.toISOString())}&to=${encodeURIComponent(to.toISOString())}`;

export function createRecorderApi(client: ApiClient): RecorderApi {
  const absolute = (path: string) => new URL(client.baseUrl + path, window.location.origin).toString();

  return {
    auth: {
      me: () => client.get('/api/auth/me'),
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
      segments: (id, from, to) => client.get(`/api/cameras/${segment(id)}/segments?${range(from, to)}`),
      start: (id) => client.post(`/api/cameras/${segment(id)}/start`),
      stop: (id) => client.post(`/api/cameras/${segment(id)}/stop`),
    },
    layouts: {
      list: () => client.get('/api/layouts'),
      create: (layout) => client.post('/api/layouts', layout),
      update: (id, layout) => client.put(`/api/layouts/${segment(id)}`, layout),
      remove: (id) => client.delete(`/api/layouts/${segment(id)}`),
    },
    events: {
      camera: (id, from, to) => client.get(`/api/camera-events?cameraId=${segment(id)}&${range(from, to)}&limit=1000`),
      plugin: (id, from, to) =>
        client.get(`/api/events?cameraId=${segment(id)}&since=${encodeURIComponent(from.toISOString())}&until=${encodeURIComponent(to.toISOString())}&limit=1000`),
    },
    media: {
      liveUrl: (id) => absolute(`/api/cameras/${segment(id)}/live.m3u8`),
      recordingsUrl: (id, from, to) => absolute(`/api/cameras/${segment(id)}/recordings.m3u8?${range(from, to)}`),
      clipDownloadUrl: (id, from, to) => {
        const token = client.token;
        return absolute(`/api/cameras/${segment(id)}/clip?${range(from, to)}${token ? `&access_token=${encodeURIComponent(token)}` : ''}`);
      },
      token: () => client.token,
    },
    users: {
      list: () => client.get('/api/users'),
      create: (user) => client.post('/api/users', user),
      update: (id, user) => client.put(`/api/users/${segment(id)}`, user),
      remove: (id) => client.delete(`/api/users/${segment(id)}`),
    },
  };
}
