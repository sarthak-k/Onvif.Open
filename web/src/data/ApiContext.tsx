import { createContext, useContext, useMemo, type ReactNode } from 'react';
import type { ApiClient } from '../api/client';
import { createRecorderApi, type RecorderApi } from '../api/recorder';

interface Services {
  client: ApiClient;
  api: RecorderApi;
}

const ApiContext = createContext<Services | null>(null);

/** Provides the recorder's API to the tree (the app's composition root decides the implementation). */
export function ApiProvider({ client, api, children }: { client: ApiClient; api?: RecorderApi; children: ReactNode }) {
  const services = useMemo(() => ({ client, api: api ?? createRecorderApi(client) }), [client, api]);
  return <ApiContext.Provider value={services}>{children}</ApiContext.Provider>;
}

function useServices(): Services {
  const services = useContext(ApiContext);
  if (!services) throw new Error('ApiProvider is missing.');
  return services;
}

export const useApi = (): RecorderApi => useServices().api;
export const useApiClient = (): ApiClient => useServices().client;
