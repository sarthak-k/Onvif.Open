import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useApi } from './ApiContext';
import { queryKeys } from './queryKeys';

/** Exports; refreshed every 3 s while one is being made. */
export function useExports() {
  const api = useApi();
  return useQuery({
    queryKey: queryKeys.exports,
    queryFn: api.exports.list,
    refetchInterval: (query) => (query.state.data?.some((e) => e.status === 'Queued' || e.status === 'Running') ? 3000 : false),
  });
}

/** One export per camera of the same stretch of time. */
export function useCreateExports() {
  const api = useApi();
  const queries = useQueryClient();
  return useMutation({
    mutationFn: ({ cameraIds, from, to, caseId }: { cameraIds: string[]; from: number; to: number; caseId?: string | null }) =>
      Promise.all(cameraIds.map((id) => api.exports.create(id, new Date(from), new Date(to), caseId))),
    onSuccess: () => queries.invalidateQueries({ queryKey: queryKeys.exports }),
  });
}

export function useShareLinks(exportId: string) {
  const api = useApi();
  return useQuery({ queryKey: [...queryKeys.exports, exportId, 'links'], queryFn: () => api.exports.links(exportId) });
}

export function useShare(exportId: string) {
  const api = useApi();
  const queries = useQueryClient();
  return useMutation({
    mutationFn: (hours: number) => api.exports.share(exportId, hours),
    onSuccess: () => queries.invalidateQueries({ queryKey: [...queryKeys.exports, exportId, 'links'] }),
  });
}

export function useRevoke(exportId: string) {
  const api = useApi();
  const queries = useQueryClient();
  return useMutation({
    mutationFn: (linkId: string) => api.exports.revoke(exportId, linkId),
    onSettled: () => queries.invalidateQueries({ queryKey: [...queryKeys.exports, exportId, 'links'] }),
  });
}
