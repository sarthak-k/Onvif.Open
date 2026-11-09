import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { GatewayRequest } from '../api/types';
import { useApi } from './ApiContext';
import { queryKeys } from './queryKeys';

/** The organization's gateways; refreshed every 30 s, as often as they report. */
export function useGateways() {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.gateways, queryFn: api.gateways.list, refetchInterval: 30_000 });
}

export function useUpdateGateway() {
  const api = useApi();
  const queries = useQueryClient();
  return useMutation({
    mutationFn: ({ id, gateway }: { id: string; gateway: GatewayRequest }) => api.gateways.update(id, gateway),
    onSuccess: () => queries.invalidateQueries({ queryKey: queryKeys.gateways }),
  });
}
