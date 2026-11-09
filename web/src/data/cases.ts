import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { CaseRequest, CaseStatus, ClipRequest } from '../api/types';
import { useApi } from './ApiContext';
import { queryKeys } from './queryKeys';

export function useCases(status?: CaseStatus) {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.cases.list(status ?? 'all'), queryFn: () => api.cases.list(status) });
}

export function useCase(id: string) {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.cases.one(id), queryFn: () => api.cases.get(id) });
}

function useInvalidateCases() {
  const queries = useQueryClient();
  return () => queries.invalidateQueries({ queryKey: queryKeys.cases.all });
}

export function useCreateCase() {
  const api = useApi();
  const invalidate = useInvalidateCases();
  return useMutation({ mutationFn: (request: CaseRequest) => api.cases.create(request), onSuccess: invalidate });
}

export function useUpdateCase(id: string) {
  const api = useApi();
  const invalidate = useInvalidateCases();
  return useMutation({ mutationFn: (request: CaseRequest) => api.cases.update(id, request), onSuccess: invalidate });
}

export function useDeleteCase() {
  const api = useApi();
  const invalidate = useInvalidateCases();
  return useMutation({ mutationFn: (id: string) => api.cases.remove(id), onSuccess: invalidate });
}

export function useAddClip() {
  const api = useApi();
  const invalidate = useInvalidateCases();
  return useMutation({ mutationFn: ({ caseId, clip }: { caseId: string; clip: ClipRequest }) => api.cases.addClip(caseId, clip), onSuccess: invalidate });
}

export function useUpdateClip(caseId: string) {
  const api = useApi();
  const invalidate = useInvalidateCases();
  return useMutation({ mutationFn: ({ clipId, note }: { clipId: string; note: string | null }) => api.cases.updateClip(caseId, clipId, note), onSuccess: invalidate });
}

export function useRemoveClip(caseId: string) {
  const api = useApi();
  const invalidate = useInvalidateCases();
  return useMutation({ mutationFn: (clipId: string) => api.cases.removeClip(caseId, clipId), onSettled: invalidate });
}
