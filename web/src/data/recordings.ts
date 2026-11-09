import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import type { EventQuery, StoredEvent } from '../api/types';
import { useApi } from './ApiContext';
import { queryKeys } from './queryKeys';

export const eventPageSize = 50;

/** Stored events matching a search, newest first, a page at a time. */
export function useEventSearch(filter: Omit<EventQuery, 'before' | 'limit'>) {
  const api = useApi();
  return useInfiniteQuery({
    queryKey: queryKeys.events.search(filter),
    queryFn: ({ pageParam }) => api.events.list({ ...filter, before: pageParam, limit: eventPageSize }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (page: StoredEvent[]) => (page.length < eventPageSize ? undefined : page[page.length - 1].startTime),
  });
}

/** Events of a site in [from, to), for the timeline. */
export function useEventsBetween(siteId: string | null | undefined, from: number, to: number) {
  const api = useApi();
  return useQuery({
    queryKey: queryKeys.events.between(siteId ?? '', from, to),
    queryFn: () => api.events.list({ siteId: siteId ?? undefined, from: new Date(from).toISOString(), to: new Date(to).toISOString(), limit: 500 }),
    enabled: from < to,
  });
}

/** Recorded files of a camera in [from, to]. */
export function useSegments(cameraId: string, from: number, to: number) {
  const api = useApi();
  return useQuery({
    queryKey: queryKeys.cameras.segments(cameraId, from, to),
    queryFn: () => api.recording.segments(cameraId, new Date(from), new Date(to)),
    refetchInterval: 30_000,
  });
}
