import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { UserRequest } from '../api/types';
import { useApi } from './ApiContext';
import { queryKeys } from './queryKeys';

export function useUsers() {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.users.all, queryFn: api.users.list });
}

function useInvalidateUsers() {
  const queries = useQueryClient();
  return () => queries.invalidateQueries({ queryKey: queryKeys.users.all });
}

export function useCreateUser() {
  const api = useApi();
  const invalidate = useInvalidateUsers();
  return useMutation({ mutationFn: (user: UserRequest) => api.users.create(user), onSuccess: invalidate });
}

export function useUpdateUser() {
  const api = useApi();
  const invalidate = useInvalidateUsers();
  return useMutation({ mutationFn: ({ id, user }: { id: string; user: UserRequest }) => api.users.update(id, user), onSettled: invalidate });
}

export function useDeleteUser() {
  const api = useApi();
  const invalidate = useInvalidateUsers();
  return useMutation({ mutationFn: (id: string) => api.users.remove(id), onSettled: invalidate });
}
