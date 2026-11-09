import { useInfiniteQuery } from '@tanstack/react-query';
import type { AuditEntry, AuditQuery } from '../api/types';
import { useApi } from './ApiContext';
import { queryKeys } from './queryKeys';

export const auditPageSize = 50;

/** The audit log, newest first, a page at a time (each page starts before the last entry of the one before). */
export function useAuditLog(filter: Pick<AuditQuery, 'action' | 'userId'>) {
  const api = useApi();
  return useInfiniteQuery({
    queryKey: queryKeys.audit(filter),
    queryFn: ({ pageParam }) => api.audit.list({ ...filter, before: pageParam, limit: auditPageSize }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (page: AuditEntry[]) => (page.length < auditPageSize ? undefined : page[page.length - 1].time),
  });
}
