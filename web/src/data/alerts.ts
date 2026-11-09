import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { AlertRuleRequest } from '../api/types';
import { useApi } from './ApiContext';
import { queryKeys } from './queryKeys';

/** How often the open alerts are checked (badge, desktop notifications). */
export const alertPollMs = 30_000;

export function useAlerts(status?: 'open') {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.alerts.list(status ?? 'all'), queryFn: () => api.alerts.list(status), refetchInterval: alertPollMs });
}

export function useAlertSummary(enabled = true) {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.alerts.summary, queryFn: api.alerts.summary, refetchInterval: alertPollMs, enabled });
}

export function useAcknowledge() {
  const api = useApi();
  const queries = useQueryClient();
  return useMutation({ mutationFn: (id: string) => api.alerts.acknowledge(id), onSettled: () => queries.invalidateQueries({ queryKey: queryKeys.alerts.all }) });
}

export function useAlertRules(enabled = true) {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.alertRules, queryFn: api.alertRules.list, enabled });
}

export function useSaveAlertRule() {
  const api = useApi();
  const queries = useQueryClient();
  return useMutation({
    mutationFn: ({ id, rule }: { id: string | null; rule: AlertRuleRequest }) => (id ? api.alertRules.update(id, rule) : api.alertRules.create(rule)),
    onSuccess: () => queries.invalidateQueries({ queryKey: queryKeys.alertRules }),
  });
}

export function useDeleteAlertRule() {
  const api = useApi();
  const queries = useQueryClient();
  return useMutation({ mutationFn: (id: string) => api.alertRules.remove(id), onSettled: () => queries.invalidateQueries({ queryKey: queryKeys.alertRules }) });
}

export function useTestAlertRule() {
  const api = useApi();
  return useMutation({ mutationFn: (id: string) => api.alertRules.test(id) });
}
