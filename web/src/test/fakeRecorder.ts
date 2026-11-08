import type { TokenStorage } from '../api/client';

type Handler = (request: { method: string; path: string; body: unknown; token: string | null }) => { status?: number; body?: unknown } | undefined;

/** A fetch replacement that answers like the recorder; routes are "METHOD /path". */
export function fakeFetch(routes: Record<string, Handler>) {
  const calls: { method: string; path: string; body: unknown; token: string | null }[] = [];

  const fetcher = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://recorder');
    const method = init?.method ?? 'GET';
    const token = ((init?.headers as Record<string, string> | undefined)?.Authorization ?? '').replace(/^Bearer /, '') || null;
    const body = init?.body ? JSON.parse(String(init.body)) : undefined;
    const request = { method, path: url.pathname, body, token };
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

export const admin = {
  id: 'u1',
  userName: 'admin',
  role: 'Admin' as const,
  enabled: true,
  mustChangePassword: false,
  createdAt: '2026-11-01T00:00:00Z',
};
