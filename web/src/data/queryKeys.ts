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
  cloud: ['cloud'] as const,
  organization: ['organization'] as const,
  singleSignOn: ['organization', 'sso'] as const,
  billing: ['billing'] as const,
  sites: ['sites'] as const,
  policies: ['recording-policies'] as const,
  audit: (filter: object) => ['audit', filter] as const,
  gateways: ['gateways'] as const,
  exports: ['exports'] as const,
  alertRules: ['alert-rules'] as const,
  ptz: {
    status: (cameraId: string) => ['ptz', cameraId, 'status'] as const,
    presets: (cameraId: string) => ['ptz', cameraId, 'presets'] as const,
  },
  alerts: {
    all: ['alerts'] as const,
    list: (status: string) => ['alerts', 'list', status] as const,
    summary: ['alerts', 'summary'] as const,
  },
  cases: {
    all: ['cases'] as const,
    list: (status: string) => ['cases', 'list', status] as const,
    one: (id: string) => ['cases', 'one', id] as const,
  },
  events: {
    search: (filter: object) => ['events', 'search', filter] as const,
    between: (siteId: string, from: number, to: number) => ['events', 'between', siteId, from, to] as const,
    since: (from: number) => ['events', 'since', from] as const,
  },
};
