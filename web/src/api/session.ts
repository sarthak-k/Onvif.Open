import { isUnauthorized, ApiError } from './errors';
import type { TokenStorage } from './tokenStorage';
import type { Tokens } from './types';

/** Exchanges a refresh token for new tokens (the recorder's /api/auth/refresh). */
export type Renew = (refreshToken: string) => Promise<Tokens>;

/**
 * The signed-in session: the access token in memory, the refresh token in storage. Renews a minute before expiry,
 * shares one renewal between concurrent callers, and tells observers when the session ends.
 */
export class Session {
  private accessToken: string | null = null;
  private refreshToken: string | null;
  private persistent: boolean;
  private renewal: Promise<boolean> | null = null;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private readonly endedListeners = new Set<() => void>();

  constructor(
    private readonly storage: TokenStorage,
    private readonly renew: Renew,
  ) {
    this.refreshToken = storage.read();
    this.persistent = storage.isPersistent();
  }

  get active(): boolean {
    return this.accessToken !== null || this.refreshToken !== null;
  }

  get token(): string | null {
    return this.accessToken;
  }

  /** An access token, renewing first when only a refresh token is known (a resumed session). */
  async ensureToken(): Promise<string | null> {
    if (!this.accessToken && this.refreshToken) await this.refresh();
    return this.accessToken;
  }

  start(tokens: Tokens, persistent = this.persistent): void {
    this.persistent = persistent;
    this.accessToken = tokens.accessToken;
    this.refreshToken = tokens.refreshToken;
    this.storage.write(tokens.refreshToken, persistent);

    clearTimeout(this.timer);
    const renewIn = Math.max(tokens.expiresIn - 60, 10) * 1000;
    this.timer = setTimeout(() => void this.refresh().catch(() => undefined), renewIn);
  }

  /** New tokens; false (and the session ended) when the refresh token is no longer accepted. */
  refresh(): Promise<boolean> {
    const refreshToken = this.refreshToken;
    if (!refreshToken) return Promise.resolve(false);

    this.renewal ??= this.renew(refreshToken)
      .then((tokens) => {
        this.start(tokens);
        return true;
      })
      .catch((error: unknown) => {
        if (isUnauthorized(error) || (error instanceof ApiError && error.status === 400)) {
          this.end();
          return false;
        }
        throw error;
      })
      .finally(() => {
        this.renewal = null;
      });

    return this.renewal;
  }

  end(): void {
    const wasActive = this.active;
    clearTimeout(this.timer);
    this.accessToken = null;
    this.refreshToken = null;
    this.storage.write(null, false);
    if (wasActive) this.endedListeners.forEach((listener) => listener());
  }

  onEnded(listener: () => void): () => void {
    this.endedListeners.add(listener);
    return () => this.endedListeners.delete(listener);
  }
}
