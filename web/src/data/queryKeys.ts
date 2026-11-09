// Query keys in one place (factory), so invalidation always matches the queries it means.
export const queryKeys = {
  cameras: {
    all: ['cameras'] as const,
    configs: () => [...queryKeys.cameras.all, 'configs'] as const,
    config: (id: string) => [...queryKeys.cameras.all, 'configs', id] as const,
    status: () => [...queryKeys.cameras.all, 'status'] as const,
    segments: (id: string, from: number, to: number) => [...queryKeys.cameras.all, 'segments', id, from, to] as const,
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
  gateways: ['gateways'] as const,
  events: {
    search: (filter: object) => ['events', 'search', filter] as const,
    between: (siteId: string, from: number, to: number) => ['events', 'between', siteId, from, to] as const,
  },
};
