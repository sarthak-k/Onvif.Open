import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Navigate, Route, Routes } from 'react-router-dom';
import type { ApiClient } from './api/client';
import { ApiError } from './api/client';
import { AuthProvider } from './auth/AuthContext';
import { RequireAuth, RequireRole } from './auth/RequireAuth';
import { Layout } from './components/Layout';
import { AccountPage } from './pages/AccountPage';
import { CameraEditorPage } from './pages/CameraEditorPage';
import { CamerasPage } from './pages/CamerasPage';
import { ChangePasswordPage } from './pages/ChangePasswordPage';
import { LoginPage } from './pages/LoginPage';
import { UsersPage } from './pages/UsersPage';

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        // Client errors (403, 404) do not get better by asking again.
        retry: (count, error) => count < 2 && !(error instanceof ApiError && error.status >= 400 && error.status < 500),
        refetchOnWindowFocus: false,
      },
    },
  });
}

export function App({ client, queryClient }: { client: ApiClient; queryClient: QueryClient }) {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider client={client}>
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
            <Route path="/cameras" element={<CamerasPage />} />
            <Route
              path="/cameras/new"
              element={
                <RequireRole role="Admin">
                  <CameraEditorPage />
                </RequireRole>
              }
            />
            <Route
              path="/cameras/:id"
              element={
                <RequireRole role="Admin">
                  <CameraEditorPage />
                </RequireRole>
              }
            />
            <Route
              path="/users"
              element={
                <RequireRole role="Admin">
                  <UsersPage />
                </RequireRole>
              }
            />
            <Route path="/account" element={<AccountPage />} />
          </Route>
          <Route path="*" element={<Navigate to="/cameras" replace />} />
        </Routes>
      </AuthProvider>
    </QueryClientProvider>
  );
}
