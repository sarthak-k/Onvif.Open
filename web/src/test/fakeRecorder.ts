import type { TokenStorage } from '../api/client';

type Handler = (request: { method: string; path: string; query: URLSearchParams; body: unknown; token: string | null }) => { status?: number; body?: unknown } | undefined;

/** A fetch replacement that answers like the recorder; routes are "METHOD /path". */
export function fakeFetch(routes: Record<string, Handler>) {
  const calls: { method: string; path: string; query: URLSearchParams; body: unknown; token: string | null }[] = [];

  const fetcher = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://recorder');
    const method = init?.method ?? 'GET';
    const token = ((init?.headers as Record<string, string> | undefined)?.Authorization ?? '').replace(/^Bearer /, '') || null;
    const body = init?.body ? JSON.parse(String(init.body)) : undefined;
    const request = { method, path: url.pathname, query: url.searchParams, body, token };
    calls.push(request);

    const handler = routes[`${method} ${url.pathname}`];
    const answer = handler ? handler(request) : { status: 404 };
    const status = answer?.status ?? 200;
    return new Response(answer?.body === undefined ? null : JSON.stringify(answer.body), {
      status,
      headers: { 'Content-Type': 'application/json' },
    });
  }) as typeof fetch;

  return { fetcher, calls };
}

export function memoryStorage(initial: string | null = null): TokenStorage & { value: string | null; persistent: boolean } {
  return {
    value: initial,
    persistent: false,
    read() {
      return this.value;
    },
    isPersistent() {
      return this.persistent;
    },
    write(token, persistent) {
      this.value = token;
      this.persistent = persistent;
    },
  };
}

export const tokens = (n: number) => ({ tokenType: 'Bearer', accessToken: `access-${n}`, expiresIn: 900, refreshToken: `refresh-${n}` });

export const organization = { id: 'org-1', name: 'Riverside Shops', region: 'us' as const };

export const mainSite = { id: 'site-main', name: 'Main St', timeZone: 'UTC', address: null };
export const lyonSite = { id: 'site-lyon', name: 'Lyon', timeZone: 'Europe/Paris', address: null };

export const policies = [
  { id: 'p-always', name: 'Always record', mode: 'Always' as const, preRollSeconds: 5, postRollSeconds: 10, continuousRetentionDays: 7, eventRetentionDays: 30, isDefault: true, schedule: [] },
  { id: 'p-events', name: 'Events only', mode: 'EventsOnly' as const, preRollSeconds: 5, postRollSeconds: 10, continuousRetentionDays: 7, eventRetentionDays: 30, isDefault: false, schedule: [] },
];

/** The signed-in user (/api/auth/me): an administrator who sees every site. */
export const admin = {
  id: 'u1',
  userName: 'admin',
  role: 'Admin' as const,
  sites: [] as string[],
  enabled: true,
  mustChangePassword: false,
  createdAt: '2026-11-01T00:00:00Z',
  organization,
};

/** Routes every signed-in page needs: sites and policies. */
export const tenancyRoutes = {
  'GET /api/sites': () => ({ body: [mainSite, lyonSite] }),
  'GET /api/recording-policies': () => ({ body: policies }),
  'GET /api/setup': () => ({ body: { required: false, tokenRequired: false, regions: [] } }),
  'GET /api/cloud': () => ({ body: { enabled: false, region: null, signup: false, regions: null } }),
};
