import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { LayoutRequest } from '../api/types';
import { useApi } from './ApiContext';
import { queryKeys } from './queryKeys';

export function useLayouts() {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.layouts.all, queryFn: api.layouts.list });
}

function useInvalidateLayouts() {
  const queries = useQueryClient();
  return () => queries.invalidateQueries({ queryKey: queryKeys.layouts.all });
}

/** Saves a new layout (no id) or changes the layout with that id. */
export function useSaveLayout() {
  const api = useApi();
  const invalidate = useInvalidateLayouts();
  return useMutation({
    mutationFn: ({ id, layout }: { id?: string; layout: LayoutRequest }) => (id ? api.layouts.update(id, layout) : api.layouts.create(layout)),
    onSuccess: invalidate,
  });
}

export function useDeleteLayout() {
  const api = useApi();
  const invalidate = useInvalidateLayouts();
  return useMutation({ mutationFn: (id: string) => api.layouts.remove(id), onSuccess: invalidate });
}
