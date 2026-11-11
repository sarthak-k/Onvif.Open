import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { RecordingPolicyRequest, SiteRequest } from '../api/types';
import { useApi } from './ApiContext';
import { queryKeys } from './queryKeys';

export function useSetupStatus() {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.setup, queryFn: api.setup.status, staleTime: 0 });
}

/** The cloud deployment this is (if any): its region and whether people can sign up. */
export function useCloud() {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.cloud, queryFn: api.setup.cloud, staleTime: Infinity });
}

export function useOrganization() {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.organization, queryFn: api.organization.get });
}

export function useRequireTwoFactor() {
  const api = useApi();
  const queries = useQueryClient();
  return useMutation({ mutationFn: (required: boolean) => api.organization.requireTwoFactor(required), onSuccess: () => queries.invalidateQueries({ queryKey: queryKeys.organization }) });
}

export function useRenameOrganization() {
  const api = useApi();
  const queries = useQueryClient();
  return useMutation({ mutationFn: (name: string) => api.organization.rename(name), onSuccess: () => queries.invalidateQueries({ queryKey: queryKeys.organization }) });
}

/** The sites the signed-in user may see. */
export function useSites() {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.sites, queryFn: api.sites.list, staleTime: 60_000 });
}

export function useSaveSite() {
  const api = useApi();
  const queries = useQueryClient();
  return useMutation({
    mutationFn: ({ id, site }: { id?: string; site: SiteRequest }) => (id ? api.sites.update(id, site) : api.sites.create(site)),
    onSuccess: () => queries.invalidateQueries({ queryKey: queryKeys.sites }),
  });
}

export function useDeleteSite() {
  const api = useApi();
  const queries = useQueryClient();
  return useMutation({ mutationFn: (id: string) => api.sites.remove(id), onSettled: () => queries.invalidateQueries({ queryKey: queryKeys.sites }) });
}

export function usePolicies(enabled = true) {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.policies, queryFn: api.policies.list, enabled, staleTime: 60_000 });
}

export function useSavePolicy() {
  const api = useApi();
  const queries = useQueryClient();
  return useMutation({
    mutationFn: ({ id, policy }: { id?: string; policy: RecordingPolicyRequest }) => (id ? api.policies.update(id, policy) : api.policies.create(policy)),
    // A changed policy changes how its cameras record.
    onSuccess: () => Promise.all([queries.invalidateQueries({ queryKey: queryKeys.policies }), queries.invalidateQueries({ queryKey: queryKeys.cameras.all })]),
  });
}

export function useDeletePolicy() {
  const api = useApi();
  const queries = useQueryClient();
  return useMutation({ mutationFn: (id: string) => api.policies.remove(id), onSettled: () => queries.invalidateQueries({ queryKey: queryKeys.policies }) });
}

/** A site's time zone (IANA id), once the sites are loaded; times of that site are shown in it. */
export function useSiteTimeZone(siteId: string | null | undefined): string | undefined {
  const sites = useSites();
  return sites.data?.find((s) => s.id === siteId)?.timeZone;
}
