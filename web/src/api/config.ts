export interface AppConfig {
  /** The recorder's URL, e.g. "https://recorder.example.com". Empty: the same origin (the dev server proxies /api). */
  apiBaseUrl: string;
}

/** Read at startup from /config.json, so one build runs against any recorder. */
export async function loadConfig(fetcher: typeof fetch = fetch): Promise<AppConfig> {
  try {
    const response = await fetcher('/config.json', { cache: 'no-store' });
    if (response.ok) {
      const config = (await response.json()) as Partial<AppConfig>;
      return { apiBaseUrl: (config.apiBaseUrl ?? '').replace(/\/+$/, '') };
    }
  } catch {
    // No config file: same origin.
  }
  return { apiBaseUrl: '' };
}
