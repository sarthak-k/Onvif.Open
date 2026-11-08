import { useQuery } from '@tanstack/react-query';
import { useApi } from './ApiContext';
import { queryKeys } from './queryKeys';
import type { CameraEvent, PluginEvent } from '../api/types';

/** Recorded files of a camera in [from, to]; refreshed while recording goes on. */
export function useSegments(cameraId: string | undefined, from: Date, to: Date) {
  const api = useApi();
  return useQuery({
    queryKey: queryKeys.cameras.segments(cameraId ?? '', from.getTime(), to.getTime()),
    queryFn: () => api.recording.segments(cameraId!, from, to),
    enabled: !!cameraId,
    refetchInterval: 30_000,
  });
}

export type TimelineEvent =
  | { kind: 'camera'; time: Date; event: CameraEvent }
  | { kind: 'plugin'; time: Date; event: PluginEvent };

/** Camera and plugin events of a camera in [from, to), oldest first. */
export function useTimelineEvents(cameraId: string | undefined, from: Date, to: Date) {
  const api = useApi();
  return useQuery({
    queryKey: queryKeys.cameras.events(cameraId ?? '', from.getTime(), to.getTime()),
    queryFn: async (): Promise<TimelineEvent[]> => {
      const [camera, plugin] = await Promise.all([api.events.camera(cameraId!, from, to), api.events.plugin(cameraId!, from, to)]);
      return [
        ...camera.map((event) => ({ kind: 'camera' as const, time: new Date(event.utcTime), event })),
        ...plugin.map((event) => ({ kind: 'plugin' as const, time: new Date(event.timestamp), event })),
      ].sort((a, b) => a.time.getTime() - b.time.getTime());
    },
    enabled: !!cameraId,
    refetchInterval: 30_000,
  });
}
