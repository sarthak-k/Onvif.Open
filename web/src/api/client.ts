import type { Tokens } from './types';

/** An API call that failed: the HTTP status and the recorder's messages ({ errors: [...] }, problem details or text). */
export class ApiError extends Error {
  readonly status: number;
  readonly errors: string[];

  constructor(status: number, errors: string[]) {
    super(errors.join(' ') || `Request failed (${status}).`);
    this.name = 'ApiError';
    this.status = status;
    this.errors = errors;
  }
}

/** Where the refresh token survives a reload: the tab only, or the browser ("keep me signed in"). */
export interface TokenStorage {
  read(): string | null;
  write(token: string | null, persistent: boolean): void;
}

const refreshKey = 'onvif-open.refresh';

export const browserTokenStorage: TokenStorage = {
  read() {
    return safe(() => sessionStorage.getItem(refreshKey) ?? localStorage.getItem(refreshKey)) ?? null;
  },
  write(token, persistent) {
    safe(() => {
      sessionStorage.removeItem(refreshKey);
      localStorage.removeItem(refreshKey);
      if (token) (persistent ? localStorage : sessionStorage).setItem(refreshKey, token);
    });
  },
};

function safe<T>(action: () => T): T | undefined {
  try {
    return action();
  } catch {
    return undefined;
  }
}

/**
 * Talks to the recorder. The access token lives only in memory; the refresh token in storage.
 * Tokens are renewed a minute before they expire, and once more when a call is rejected with 401.
 */
export class ApiClient {
  private accessToken: string | null = null;
  private refreshToken: string | null;
  private persistent: boolean;
  private refreshing: Promise<boolean> | null = null;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private readonly listeners = new Set<() => void>();

  constructor(
    readonly baseUrl: string,
    private readonly storage: TokenStorage = browserTokenStorage,
    private readonly fetcher: typeof fetch = (...args) => fetch(...args),
  ) {
    this.refreshToken = storage.read();
    this.persistent = safe(() => localStorage.getItem(refreshKey) !== null) ?? false;
  }

  /** True when a session can be resumed or is active. */
  get hasSession(): boolean {
    return this.accessToken !== null || this.refreshToken !== null;
  }

  /** The current access token, for media URLs (?access_token=). */
  get token(): string | null {
    return this.accessToken;
  }

  /** Called when the session ends: signed out, or the refresh token was rejected. */
  onSignedOut(listener: () => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  async signIn(userName: string, password: string, persistent: boolean): Promise<void> {
    this.persistent = persistent;
    this.setTokens(await this.send<Tokens>('POST', '/api/auth/login', { userName, password }, false));
  }

  /** The password change ends every session; the recorder answers with new tokens for this one. */
  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    this.setTokens(await this.request<Tokens>('POST', '/api/auth/password', { currentPassword, newPassword }));
  }

  signOut(): void {
    this.clear();
  }

  /** Renews the tokens; false (and signed out) when the refresh token is no longer accepted. */
  refresh(): Promise<boolean> {
    if (!this.refreshToken) return Promise.resolve(false);

    this.refreshing ??= this.send<Tokens>('POST', '/api/auth/refresh', { refreshToken: this.refreshToken }, false)
      .then((tokens) => {
        this.setTokens(tokens);
        return true;
      })
      .catch((error: unknown) => {
        if (error instanceof ApiError && (error.status === 401 || error.status === 400)) {
          this.clear();
          return false;
        }
        throw error;
      })
      .finally(() => {
        this.refreshing = null;
      });

    return this.refreshing;
  }

  get<T>(path: string): Promise<T> {
    return this.request<T>('GET', path);
  }

  post<T>(path: string, body?: unknown): Promise<T> {
    return this.request<T>('POST', path, body);
  }

  put<T>(path: string, body?: unknown): Promise<T> {
    return this.request<T>('PUT', path, body);
  }

  delete(path: string): Promise<void> {
    return this.request<void>('DELETE', path);
  }

  /** An authenticated call: refreshes first when there is no access token yet, and retries once after a 401. */
  async request<T>(method: string, path: string, body?: unknown): Promise<T> {
    if (!this.accessToken && this.refreshToken) await this.refresh();

    try {
      return await this.send<T>(method, path, body, true);
    } catch (error) {
      if (error instanceof ApiError && error.status === 401 && (await this.refresh())) {
        return this.send<T>(method, path, body, true);
      }
      throw error;
    }
  }

  private async send<T>(method: string, path: string, body: unknown, authenticated: boolean): Promise<T> {
    const headers: Record<string, string> = { Accept: 'application/json' };
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    if (authenticated && this.accessToken) headers.Authorization = `Bearer ${this.accessToken}`;

    let response: Response;
    try {
      response = await this.fetcher(this.baseUrl + path, {
        method,
        headers,
        body: body === undefined ? undefined : JSON.stringify(body),
      });
    } catch {
      throw new ApiError(0, ['The recorder cannot be reached.']);
    }

    if (!response.ok) throw new ApiError(response.status, await readErrors(response));
    if (response.status === 204) return undefined as T;

    const text = await response.text();
    return (text ? JSON.parse(text) : undefined) as T;
  }

  private setTokens(tokens: Tokens): void {
    this.accessToken = tokens.accessToken;
    this.refreshToken = tokens.refreshToken;
    this.storage.write(tokens.refreshToken, this.persistent);

    clearTimeout(this.timer);
    const renewIn = Math.max(tokens.expiresIn - 60, 10) * 1000;
    this.timer = setTimeout(() => void this.refresh().catch(() => undefined), renewIn);
  }

  private clear(): void {
    const hadSession = this.hasSession;
    clearTimeout(this.timer);
    this.accessToken = null;
    this.refreshToken = null;
    this.storage.write(null, false);
    if (hadSession) this.listeners.forEach((listener) => listener());
  }
}

async function readErrors(response: Response): Promise<string[]> {
  const fallback = response.status === 403 ? ['You are not allowed to do this.'] : [`Request failed (${response.status}).`];
  const text = await response.text().catch(() => '');
  if (!text) return fallback;

  try {
    const body = JSON.parse(text) as { errors?: unknown; error?: unknown; detail?: unknown; title?: unknown };
    if (Array.isArray(body.errors)) return body.errors.map(String);
    // ASP.NET validation problem details: { errors: { field: [messages] } }
    if (body.errors && typeof body.errors === 'object') return Object.values(body.errors as Record<string, unknown>).flat().map(String);
    if (typeof body.error === 'string') return [body.error];
    if (typeof body.detail === 'string') return [body.detail];
    if (typeof body.title === 'string') return [body.title];
  } catch {
    return [text];
  }
  return fallback;
}
