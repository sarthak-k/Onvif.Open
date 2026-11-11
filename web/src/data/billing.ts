import { useMutation, useQuery } from '@tanstack/react-query';
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
