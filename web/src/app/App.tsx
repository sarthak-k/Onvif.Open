import { lazy, Suspense } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Navigate, Route, Routes } from 'react-router-dom';
import type { ApiClient } from '../api/client';
import { ApiError } from '../api/errors';
import type { RecorderApi } from '../api/recorder';
import { ApiProvider } from '../data/ApiContext';
import { AuthProvider } from '../features/auth/AuthContext';
import { RequireAuth, RequirePermission } from '../features/auth/RequireAuth';
import { PlayerProvider } from '../features/live/player/PlayerContext';
import { createHlsPlayer } from '../features/live/player/hlsPlayer';
import type { PlayerFactory } from '../features/live/player/types';
import { Layout } from './Layout';
import { appRoutes } from './routes';

const LoginPage = lazy(() => import('../features/auth/LoginPage'));
const ChangePasswordPage = lazy(() => import('../features/auth/ChangePasswordPage'));

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        // Client errors (403, 404) do not get better by asking again.
        retry: (count, error) => count < 2 && !(error instanceof ApiError && error.isClientError),
        refetchOnWindowFocus: false,
        staleTime: 10_000,
      },
    },
  });
}

interface Props {
  client: ApiClient;
  queryClient: QueryClient;
  /** Replaces the HTTP implementation of the recorder's API (tests, other back ends). */
  api?: RecorderApi;
  /** Replaces how live video is played (hls.js by default). */
  player?: PlayerFactory;
}

/** The composition root: API, cache, session, then the routes. */
export function App({ client, queryClient, api, player = createHlsPlayer }: Props) {
  return (
    <QueryClientProvider client={queryClient}>
      <ApiProvider client={client} api={api}>
        <PlayerProvider factory={player}>
          <AuthProvider>
            <Suspense fallback={<p className="loading">Loading…</p>}>
              <Routes>
                <Route path="/login" element={<LoginPage />} />
                <Route
                  path="/change-password"
                  element={
                    <RequireAuth>
                      <ChangePasswordPage />
                    </RequireAuth>
                  }
                />
                <Route
                  element={
                    <RequireAuth>
                      <Layout />
                    </RequireAuth>
                  }
                >
                  {appRoutes.map(({ path, page: Page, permission }) => (
                    <Route
                      key={path}
                      path={path}
                      element={
                        <RequirePermission permission={permission}>
                          <Page />
                        </RequirePermission>
                      }
                    />
                  ))}
                </Route>
                <Route path="*" element={<Navigate to="/cameras" replace />} />
              </Routes>
            </Suspense>
          </AuthProvider>
        </PlayerProvider>
      </ApiProvider>
    </QueryClientProvider>
  );
}
