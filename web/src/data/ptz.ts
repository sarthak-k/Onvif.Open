import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../api/errors';
import { useApi } from './ApiContext';
import { queryKeys } from './queryKeys';

/** Whether the camera pans, tilts or zooms: the recorder answers 501 for one that does not. */
export function usePtzSupport(cameraId: string) {
  const api = useApi();
  const status = useQuery({
    queryKey: queryKeys.ptz.status(cameraId),
    queryFn: () => api.ptz.status(cameraId),
    // A refusal (4xx) or "no PTZ" (501) is the answer; a camera that did not answer (502) is asked again.
    retry: (count, error) => count < 2 && !(error instanceof ApiError && (error.status < 500 || error.status === 501)),
    staleTime: 5 * 60_000,
  });
  const unsupported = status.error instanceof ApiError && status.error.status === 501;
  return { supported: status.isSuccess, unsupported, loading: status.isPending, error: unsupported ? null : status.error };
}

export function usePtzPresets(cameraId: string, enabled: boolean) {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.ptz.presets(cameraId), queryFn: () => api.ptz.presets(cameraId), enabled });
}

export function useSavePreset(cameraId: string) {
  const api = useApi();
  const queries = useQueryClient();
  return useMutation({ mutationFn: (name: string) => api.ptz.savePreset(cameraId, name), onSettled: () => queries.invalidateQueries({ queryKey: queryKeys.ptz.presets(cameraId) }) });
}

export function useRemovePreset(cameraId: string) {
  const api = useApi();
  const queries = useQueryClient();
  return useMutation({ mutationFn: (token: string) => api.ptz.removePreset(cameraId, token), onSettled: () => queries.invalidateQueries({ queryKey: queryKeys.ptz.presets(cameraId) }) });
}
