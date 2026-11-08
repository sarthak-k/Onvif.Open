import type { ApiClient } from './client';
import type { CameraConfig, CameraStatus, DiscoveredDevice, ProbeResult, User, UserRequest } from './types';

const id = (value: string) => encodeURIComponent(value);

/** The recorder's endpoints the app uses, grouped by area. */
export function createRecorderApi(client: ApiClient) {
  return {
    me: () => client.get<User>('/api/auth/me'),

    cameras: {
      list: () => client.get<CameraConfig[]>('/api/config/cameras'),
      get: (cameraId: string) => client.get<CameraConfig>(`/api/config/cameras/${id(cameraId)}`),
      create: (camera: CameraConfig) => client.post<CameraConfig>('/api/config/cameras', camera),
      update: (cameraId: string, camera: CameraConfig) => client.put<CameraConfig>(`/api/config/cameras/${id(cameraId)}`, camera),
      remove: (cameraId: string) => client.delete(`/api/config/cameras/${id(cameraId)}`),
      /** Connects to the camera without saving it. A null password with an id uses the stored one. */
      test: (camera: Partial<CameraConfig>) => client.post<ProbeResult>('/api/config/cameras/test', camera),
      status: () => client.get<CameraStatus[]>('/api/cameras'),
      discover: () => client.get<DiscoveredDevice[]>('/api/discovery'),
    },

    users: {
      list: () => client.get<User[]>('/api/users'),
      create: (user: UserRequest) => client.post<User>('/api/users', user),
      update: (userId: string, user: UserRequest) => client.put<User>(`/api/users/${id(userId)}`, user),
      remove: (userId: string) => client.delete(`/api/users/${id(userId)}`),
    },
  };
}

export type RecorderApi = ReturnType<typeof createRecorderApi>;
