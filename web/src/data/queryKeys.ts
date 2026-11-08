// Query keys in one place (factory), so invalidation always matches the queries it means.
export const queryKeys = {
  cameras: {
    all: ['cameras'] as const,
    configs: () => [...queryKeys.cameras.all, 'configs'] as const,
    config: (id: string) => [...queryKeys.cameras.all, 'configs', id] as const,
    status: () => [...queryKeys.cameras.all, 'status'] as const,
  },
  users: {
    all: ['users'] as const,
  },
};
