import type { ApiClient } from './client';
import type { CameraConfig, CameraStatus, DiscoveredDevice, ProbeResult, User, UserRequest } from './types';

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
  start(cameraId: string): Promise<void>;
  stop(cameraId: string): Promise<void>;
}

export interface UsersApi {
  list(): Promise<User[]>;
  create(user: UserRequest): Promise<User>;
  update(userId: string, user: UserRequest): Promise<User>;
  remove(userId: string): Promise<void>;
}

export interface RecorderApi {
  auth: AuthApi;
  cameras: CamerasApi;
  recording: RecordingApi;
  users: UsersApi;
}

const segment = encodeURIComponent;

export function createRecorderApi(client: ApiClient): RecorderApi {
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
      start: (id) => client.post(`/api/cameras/${segment(id)}/start`),
      stop: (id) => client.post(`/api/cameras/${segment(id)}/stop`),
    },
    users: {
      list: () => client.get('/api/users'),
      create: (user) => client.post('/api/users', user),
      update: (id, user) => client.put(`/api/users/${segment(id)}`, user),
      remove: (id) => client.delete(`/api/users/${segment(id)}`),
    },
  };
}
