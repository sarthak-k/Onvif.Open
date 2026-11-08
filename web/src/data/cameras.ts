import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { CameraConfig } from '../api/types';
import { useApi } from './ApiContext';
import { queryKeys } from './queryKeys';

const statusRefreshMs = 5000;

export function useCameraConfigs(enabled = true) {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.cameras.configs(), queryFn: api.cameras.list, enabled });
}

export function useCameraConfig(id: string | undefined) {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.cameras.config(id ?? ''), queryFn: () => api.cameras.get(id!), enabled: id !== undefined });
}

/** Live recording state; polled while the page is visible. */
export function useCameraStatus() {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.cameras.status(), queryFn: api.recording.status, refetchInterval: statusRefreshMs });
}

function useInvalidateCameras() {
  const queries = useQueryClient();
  return () => queries.invalidateQueries({ queryKey: queryKeys.cameras.all });
}

/** Adds a camera (no id) or changes the camera with that id. */
export function useSaveCamera(id: string | undefined) {
  const api = useApi();
  const invalidate = useInvalidateCameras();
  return useMutation({
    mutationFn: (camera: CameraConfig) => (id === undefined ? api.cameras.create(camera) : api.cameras.update(id, camera)),
    onSuccess: invalidate,
  });
}

export function useDeleteCamera() {
  const api = useApi();
  const invalidate = useInvalidateCameras();
  return useMutation({ mutationFn: (id: string) => api.cameras.remove(id), onSettled: invalidate });
}

export function useRecordingControl() {
  const api = useApi();
  const invalidate = useInvalidateCameras();
  return useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'start' | 'stop' }) => api.recording[action](id),
    onSettled: invalidate,
  });
}

export function useCameraTest() {
  const api = useApi();
  return useMutation({ mutationFn: (camera: CameraConfig) => api.cameras.test(camera) });
}

export function useDiscovery() {
  const api = useApi();
  return useMutation({ mutationFn: () => api.cameras.discover() });
}
