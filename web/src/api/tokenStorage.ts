/** Where the refresh token survives a reload (strategy): the tab only, or the browser ("keep me signed in"). */
export interface TokenStorage {
  read(): string | null;
  /** True when the stored token is kept across browser restarts. */
  isPersistent(): boolean;
  write(token: string | null, persistent: boolean): void;
}

const key = 'onvif-open.refresh';

function attempt<T>(action: () => T): T | undefined {
  try {
    return action();
  } catch {
    // Storage disabled (private mode, blocked cookies): the session lasts as long as the page.
    return undefined;
  }
}

export const browserTokenStorage: TokenStorage = {
  read: () => attempt(() => sessionStorage.getItem(key) ?? localStorage.getItem(key)) ?? null,
  isPersistent: () => attempt(() => localStorage.getItem(key) !== null) ?? false,
  write(token, persistent) {
    attempt(() => {
      sessionStorage.removeItem(key);
      localStorage.removeItem(key);
      if (token) (persistent ? localStorage : sessionStorage).setItem(key, token);
    });
  },
};
