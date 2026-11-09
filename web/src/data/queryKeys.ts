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
  layouts: {
    all: ['layouts'] as const,
  },
  setup: ['setup'] as const,
  organization: ['organization'] as const,
  sites: ['sites'] as const,
  policies: ['recording-policies'] as const,
  audit: (filter: object) => ['audit', filter] as const,
};
