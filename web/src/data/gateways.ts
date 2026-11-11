import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { GatewayRequest } from '../api/types';
import { useApi } from './ApiContext';
import { queryKeys } from './queryKeys';

/** The organization's gateways; refreshed every 30 s, as often as they report (every 5 s while a cloud gateway starts). */
export function useGateways() {
  const api = useApi();
  return useQuery({
    queryKey: queryKeys.gateways,
    queryFn: api.gateways.list,
    refetchInterval: (query) => (query.state.data?.some((g) => g.hosting?.state === 'Starting') ? 5_000 : 30_000),
  });
}

export function useCreateCloudGateway() {
  const api = useApi();
  const queries = useQueryClient();
  return useMutation({ mutationFn: (name: string | null) => api.gateways.createCloud(name), onSuccess: () => queries.invalidateQueries({ queryKey: queryKeys.gateways }) });
}

export function useStartGateway() {
  const api = useApi();
  const queries = useQueryClient();
  return useMutation({ mutationFn: (id: string) => api.gateways.start(id), onSettled: () => queries.invalidateQueries({ queryKey: queryKeys.gateways }) });
}

export function useUpdateGateway() {
  const api = useApi();
  const queries = useQueryClient();
  return useMutation({
    mutationFn: ({ id, gateway }: { id: string; gateway: GatewayRequest }) => api.gateways.update(id, gateway),
    onSuccess: () => queries.invalidateQueries({ queryKey: queryKeys.gateways }),
  });
}

export function usePairGateway() {
  const api = useApi();
  const queries = useQueryClient();
  return useMutation({
    mutationFn: ({ code, name }: { code: string; name: string | null }) => api.gateways.pair(code, name),
    onSuccess: () => queries.invalidateQueries({ queryKey: queryKeys.gateways }),
  });
}

export function useRemoveGateway() {
  const api = useApi();
  const queries = useQueryClient();
  return useMutation({ mutationFn: (id: string) => api.gateways.remove(id), onSettled: () => queries.invalidateQueries({ queryKey: queryKeys.gateways }) });
}
