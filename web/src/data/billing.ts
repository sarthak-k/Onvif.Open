import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { PlanRequest } from '../api/types';
import { useApi } from './ApiContext';
import { queryKeys } from './queryKeys';
import { browser } from '../shared/browser';

/** The organization's plan, subscription and usage. */
export function useBilling() {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.billing, queryFn: api.billing.get, staleTime: 30_000 });
}

/** Off to the payment provider's checkout for a plan (it sends people back to this page). */
export function useCheckout() {
  const api = useApi();
  return useMutation({
    mutationFn: (planId: string) => api.billing.checkout(planId, window.location.origin + window.location.pathname),
    onSuccess: ({ url }) => browser.leave(url),
  });
}

/** Off to the payment provider's page for the subscription. */
export function useBillingPortal() {
  const api = useApi();
  return useMutation({
    mutationFn: () => api.billing.portal(window.location.origin + window.location.pathname),
    onSuccess: ({ url }) => browser.leave(url),
  });
}

/** Every plan and version (the operator's owners). */
export function usePlans() {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.plans, queryFn: api.billing.plans });
}

export function useSavePlan() {
  const api = useApi();
  const queries = useQueryClient();
  return useMutation({
    mutationFn: ({ id, plan }: { id?: string; plan: PlanRequest }) => (id ? api.billing.updatePlan(id, plan) : api.billing.createPlan(plan)),
    onSettled: () => queries.invalidateQueries({ queryKey: queryKeys.billing }),
  });
}

export function useRetirePlan() {
  const api = useApi();
  const queries = useQueryClient();
  return useMutation({ mutationFn: (id: string) => api.billing.retirePlan(id), onSettled: () => queries.invalidateQueries({ queryKey: queryKeys.billing }) });
}
